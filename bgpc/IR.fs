﻿module IR

open System
open CGraph
open Common.Debug
open Common.Error

type CounterExample = 
    | UnusedPreferences of Map<int, Regex.T>
    | NoPathForRouters of Set<string>
    | InconsistentPrefs of CgState * CgState

type Match = 
    | Peer of string 
    | State of int array * string
    | PathRE of Regex.T
    | NoMatch

    override this.ToString () = 
        match this with 
        | Peer s -> "Peer=" + s
        | State(is,s) -> "Community=" + (List.ofArray is).ToString() + "," + s
        | PathRE r -> "Regex=" + r.ToString()
        | NoMatch -> "--"

type Action = 
    | NoAction
    | SetComm of int array * string
    | SetMed of int
    | SetLP of int
    | Originate

    override this.ToString() = 
        match this with 
        | NoAction -> ""
        | SetComm(is,s) -> "Community<-" + is.ToString() + "," + s
        | SetMed i -> "MED<-" + i.ToString()
        | SetLP i -> "LP<-" + i.ToString()
        | Originate -> "Originate"

type Actions = Action list

type Rule =
    {Import: Match;
     Export: Actions}

type T = Map<string, Rule list>

let format (config: T) = 
    let sb = System.Text.StringBuilder ()
    for kv in config do 
        sb.Append("Router ") |> ignore
        sb.Append(kv.Key) |> ignore
        for rule in kv.Value do
            sb.Append("\n  Match: ") |> ignore
            sb.Append(rule.Import.ToString()) |> ignore
            sb.Append("\n    Actions: ") |> ignore
            sb.Append(rule.Export.ToString()) |> ignore
        sb.Append("\n\n") |> ignore
    sb.ToString()

let compareLocThenPref (x,i1) (y,i2) = 
    let cmp = compare i1 i2
    if cmp = 0 then
        compare x.Node.Loc y.Node.Loc
    else cmp

let rec removeAdjacentLocs sorted = 
    match sorted with 
    | [] | [_] -> sorted
    | hd1::((hd2::z) as tl) ->
        let (x,i1) = hd1 
        let (y,i2) = hd2 
        if x.Node.Loc = y.Node.Loc then removeAdjacentLocs (hd1::z)
        else hd1 :: (removeAdjacentLocs tl)

let genConfig (cg: CGraph.T) (ord: Consistency.Ordering) : T =
    let cgRev = copyReverseGraph cg
    let neighborsIn v = seq {for e in cgRev.Graph.OutEdges v do yield e.Target}
    let neighborsOut v = seq {for e in cg.Graph.OutEdges v do yield e.Target}
    let mutable config = Map.empty
    for entry in ord do 
        let mutable rules = []
        let loc = entry.Key
        let prefs = entry.Value 
        let prefNeighborsIn =
            prefs
            |> Seq.mapi (fun i v -> (neighborsIn v, i))
            |> Seq.map (fun (ns,i) -> Seq.map (fun n -> (n,i)) ns) 
            |> Seq.fold Seq.append Seq.empty 
            |> List.ofSeq
            |> List.sortWith compareLocThenPref
            |> removeAdjacentLocs
        let mutable lp = 99
        let mutable lastPref = None
        for v, pref in prefNeighborsIn do 
            match lastPref with 
            | Some p when pref = p -> () 
            | _ ->
                lastPref <- Some pref 
                lp <- lp + 1
            let unambiguous = 
                prefNeighborsIn 
                |> Set.ofList 
                |> Set.filter (fun (x,_) -> x.Node.Loc = v.Node.Loc) 
                |> Set.count 
                |> ((=) 1)
            let m =
                match v.Node.Typ with 
                | Topology.Start -> NoMatch
                | _ -> 
                    if unambiguous then Peer v.Node.Loc
                    else State (v.States, v.Node.Loc)
            let a = 
                match v.Node.Typ with 
                | Topology.Start -> [Originate]
                | _ ->
                    if lp = 100 then [] 
                    else [SetLP(lp)]
            rules <- {Import = m; Export = a}::rules
        config <- Map.add loc rules config
    config

let compileToIR (topo: Topology.T) (reb: Regex.REBuilder) (res: Regex.T list) (debugName: string) : Result<T, CounterExample> =
    let cg = CGraph.buildFromRegex topo reb res
    debug1 (fun () -> CGraph.generatePNG cg debugName)
    (* Ensure the path suffix property and dont conside simple paths *)
    CGraph.Minimize.delMissingSuffixPaths cg
    CGraph.Minimize.minimizeO3 cg
    (* Save graphs to file *)
    debug1 (fun () -> CGraph.generatePNG cg (debugName + "-min"))
    (* Check for errors *)
    let startingLocs = List.fold (fun acc r -> Set.union (reb.StartingLocs r) acc) Set.empty res
    let originators = 
        CGraph.neighbors cg cg.Start
        |> Seq.map (fun v -> v.Node.Loc)
        |> Set.ofSeq
    let canOriginate = 
        cg.Topo.Vertices 
        |> Seq.filter Topology.canOriginateTraffic
        |> Seq.map (fun v -> v.Loc) 
        |> Set.ofSeq
    let locsThatNeedPath = Set.difference (Set.intersect startingLocs canOriginate) originators
    let locsThatGetPath = CGraph.acceptingLocations cg
    logInfo1(String.Format("Locations that need path: {0}", locsThatNeedPath.ToString()))
    logInfo1(String.Format("Locations that get path: {0}", locsThatGetPath.ToString()))
    let lost = Set.difference locsThatNeedPath locsThatGetPath
    if not (Set.isEmpty lost) then 
        Err(NoPathForRouters(lost))
    else
        (* Find unused preferences *)
        let numberedRegexes = seq {for i in 1.. List.length res do yield i}  |> Set.ofSeq
        let prefs = CGraph.preferences cg
        let unusedPrefs = Set.difference numberedRegexes prefs
        if not (Set.isEmpty unusedPrefs) then
            let cexamples = Set.fold (fun acc p -> Map.add p (List.nth res (p-1)) acc) Map.empty unusedPrefs
            Err(UnusedPreferences(cexamples))
        else
            match Consistency.findOrderingConservative cg debugName with 
            | Ok ord ->
                let config = genConfig cg ord
                debug1 (fun () -> System.IO.File.WriteAllText(debugName + ".ir", format config))
                Ok (config)
            | Err((x,y)) -> Err(InconsistentPrefs(x,y))
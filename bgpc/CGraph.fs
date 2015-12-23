﻿module CGraph

open Common.Error
open Common.Debug
open System.Collections.Generic
open System.Diagnostics
open QuickGraph
open QuickGraph.Algorithms
open QuickGraph.Algorithms

type CgState = 
    {States: int array; 
     Accept: Set<int>; 
     Node: Topology.State}
     override this.ToString() = 
        "(State=" + (List.ofArray this.States).ToString() + ", Loc=" + this.Node.Loc + ")"

type T = 
    {Start: CgState;
     End: CgState;
     Graph: BidirectionalGraph<CgState, TaggedEdge<CgState, unit>>
     Topo: Topology.T}

type Direction = Up | Down

let copyGraph (cg: T) : T = 
    let newCG = QuickGraph.BidirectionalGraph() 
    for v in cg.Graph.Vertices do newCG.AddVertex v |> ignore
    for e in cg.Graph.Edges do newCG.AddEdge e |> ignore
    {Start=cg.Start; Graph=newCG; End=cg.End; Topo=cg.Topo}

let copyReverseGraph (cg: T) : T = 
    let newCG = QuickGraph.BidirectionalGraph() 
    for v in cg.Graph.Vertices do newCG.AddVertex v |> ignore
    for e in cg.Graph.Edges do
        let e' = TaggedEdge(e.Target, e.Source, ())
        newCG.AddEdge e' |> ignore
    {Start=cg.Start; Graph=newCG; End=cg.End; Topo=cg.Topo}

let buildFromAutomata (topo: Topology.T) (autos : Regex.Automaton array) : T =
    let alphabetIn, alphabetOut = Topology.alphabet(topo)
    let alphabetAll = Set.union alphabetIn alphabetOut
    let graph = BidirectionalGraph<CgState, TaggedEdge<CgState,unit>>()
    let starting = Array.map (fun (x: Regex.Automaton) -> x.q0) autos
    let newStart = {States = starting; Accept = Set.empty; Node = {Loc="start"; Typ = Topology.Start} }
    graph.AddVertex newStart |> ignore
    let marked = HashSet(HashIdentity.Structural)
    let todo = Queue()
    todo.Enqueue newStart
    while todo.Count > 0 do
        let currState = todo.Dequeue()
        if not (marked.Contains currState) then 
            marked.Add currState |> ignore
            let {States=ss; Node=t} = currState
            let adj = 
                if t.Typ = Topology.Start then 
                    Set.filter Topology.canOriginateTraffic alphabetAll 
                else 
                    topo.OutEdges t 
                    |> Seq.map (fun e -> e.Target)
                    |> Set.ofSeq
            let adj = if t.Typ = Topology.Unknown then Set.add t adj else adj
            for c in Set.intersect alphabetAll adj do
                let nextInfo = Array.init autos.Length (fun i ->
                    let g, v = autos.[i], ss.[i]

                    (* printfn "trans: %A" g.trans
                    printfn "(v,c.Loc): (%A,%A)" v c.Loc *)
                    
                    let key = Map.findKey (fun (q,S) _ -> q = v && Set.contains c.Loc S) g.trans
                    let newState = Map.find key g.trans
                    let accept =
                        if (Topology.canOriginateTraffic c) && (Set.contains newState g.F) then 
                            Set.singleton (i+1)
                        else Set.empty
                    newState, accept)
                let nextStates, nextAccept = Array.unzip nextInfo
                let accept = Array.fold Set.union Set.empty nextAccept
                let state = {States=nextStates; Accept=accept; Node=c}
                graph.AddVertex state |> ignore
                graph.AddEdge(TaggedEdge(currState, state, ())) |> ignore
                todo.Enqueue state 
    let newEnd = {States = [||]; Accept = Set.empty; Node = {Loc="end"; Typ = Topology.End}}
    graph.AddVertex newEnd |> ignore
    let accepting = Seq.filter (fun v -> not (Set.isEmpty v.Accept)) graph.Vertices
    Seq.iter (fun v -> graph.AddEdge(TaggedEdge(v, newEnd, ())) |> ignore) accepting
    {Start=newStart; Graph=graph; End=newEnd; Topo=topo}

let buildFromRegex (reb: Regex.REBuilder) (res: Regex.T list) : T =
    (* for v in reb.Topo().Vertices do 
        printfn "Vertex: %A" v.Loc
    for e in reb.Topo().Edges do 
        printfn "Edge: (%A, %A)" e.Source.Loc e.Target.Loc *)
    res 
    |> List.map (fun r -> reb.MakeDFA (Regex.rev r))
    |> Array.ofList
    |> buildFromAutomata (reb.Topo ())

let inline preferences (cg: T) : Set<int> = 
    let mutable all = Set.empty
    for v in cg.Graph.Vertices do 
        all <- Set.union all v.Accept
    all

let inline acceptingStates (cg: T) : Set<CgState> =
    cg.Graph.Vertices
    |> Seq.filter (fun (v: CgState) -> not v.Accept.IsEmpty)
    |> Set.ofSeq

let inline acceptingLocations (cg: T) : Set<string> = 
    acceptingStates cg
    |> Set.map (fun v -> v.Node.Loc)

let inline isRealNode (state: CgState) : bool =
    Topology.isTopoNode state.Node

let inline neighbors (cg: T) (state: CgState) =
    cg.Graph.OutEdges state
    |> Seq.map (fun e -> e.Target) 

let inline neighborsIn (cg: T) (state: CgState) = 
    cg.Graph.InEdges state
    |> Seq.map (fun e -> e.Source)

let inline isUnknownRepeater (cg: T) (state: CgState) =
    let ns = neighbors cg state
    (state.Node.Typ = Topology.Unknown) &&
    (Seq.exists (fun n -> n = state) ns)


let restrict (cg: T) (i: int) : T = 
    if Set.contains i (preferences cg) then 
        let copy = copyGraph cg
        copy.Graph.RemoveVertexIf (fun v -> 
            not (v.Accept.IsEmpty) && 
            not (Set.exists (fun i' -> i' <= i) v.Accept)
        ) |> ignore
        copy
    else cg

let toDot (cg: T) : string = 
    let onFormatEdge(e: Graphviz.FormatEdgeEventArgs<CgState, TaggedEdge<CgState,unit>>) = ()
    let onFormatVertex(v: Graphviz.FormatVertexEventArgs<CgState>) = 
        let states = Array.map string v.Vertex.States |> String.concat ", "
        let location = v.Vertex.Node.Loc.ToString()
        match v.Vertex.Node.Typ with 
        | Topology.Start -> v.VertexFormatter.Label <- "Start"
        | Topology.End -> v.VertexFormatter.Label <- "End"
        | _ ->
            if Set.isEmpty v.Vertex.Accept then 
                v.VertexFormatter.Label <- "(" + states + ", " + location + ")"
            else
                v.VertexFormatter.Label <- "(" + states + ", " + location + ")" + "\npref=" + (v.Vertex.Accept.ToString ())
                v.VertexFormatter.Shape <- Graphviz.Dot.GraphvizVertexShape.DoubleCircle
                v.VertexFormatter.Style <- Graphviz.Dot.GraphvizVertexStyle.Filled
                v.VertexFormatter.FillColor <- Graphviz.Dot.GraphvizColor.LightYellow
    let graphviz = Graphviz.GraphvizAlgorithm<CgState, TaggedEdge<CgState,unit>>(cg.Graph)
    graphviz.FormatEdge.Add(onFormatEdge)
    graphviz.FormatVertex.Add(onFormatVertex)
    graphviz.Generate()

let generatePNG (cg: T) (file: string) : unit =
    System.IO.File.WriteAllText(file + ".dot", toDot cg)
    let p = new Process()
    p.StartInfo.FileName <- "dot"
    p.StartInfo.UseShellExecute <- false
    p.StartInfo.Arguments <- "-Tpng " + file + ".dot -o " + file + ".png" 
    p.StartInfo.CreateNoWindow <- true
    p.Start() |> ignore
    p.WaitForExit();


module Reachable =

    let floydWarshall (cg: T) : Map<CgState, Set<CgState>> = 
        let fw = ShortestPath.FloydWarshallAllShortestPathAlgorithm(cg.Graph, fun _ -> 1.0)
        fw.Compute ()
        let mutable reachability = Map.empty
        for src in cg.Graph.Vertices do 
            let mutable toDst = Set.singleton src
            for dst in cg.Graph.Vertices do 
                if fw.TryGetPath(src, dst, ref Seq.empty) then 
                    toDst <- Set.add dst toDst
            reachability <- Map.add src toDst reachability
        reachability

    type AnnotatedCG(cg: T) =
        let reachability = floydWarshall cg
        member this.Cg = cg
        member this.ReachInfo = reachability

    let dfs (cg: T) (source: CgState) direction : seq<CgState> = seq { 
        let f = if direction = Up then neighborsIn else neighbors
        let s = Stack()
        let marked = ref Set.empty
        s.Push source
        while s.Count > 0 do 
            let v = s.Pop()
            if not (Set.contains v !marked) then 
                marked := Set.add v !marked
                yield v
                for w in f cg v do 
                    s.Push w }

    let srcWithout (cg: T) source without direction =
        let f = if direction = Up then neighborsIn else  neighbors
        let s = Stack()
        let mutable marked = Set.empty
        s.Push source
        while s.Count > 0 do 
            let v = s.Pop()
            if not (marked.Contains v) && not (without v) then 
                marked <- Set.add v marked
                for w in f cg v do 
                    s.Push w
        marked

    let srcDstWithout (cg: T) source sink without direction = 
        if without sink || without source then false
        else Set.contains sink (srcWithout cg source without direction)

    let src (cg: T) (source: CgState) direction : Set<CgState> =
        srcWithout cg source (fun _ -> false) direction

    let srcDst (cg: T) source sink direction = 
        srcDstWithout cg source sink (fun _ -> false) direction

    let srcAcceptingWithout cg src without direction = 
        let aux acc cg = 
            Set.union cg.Accept acc
        srcWithout cg src without direction |> Set.fold aux Set.empty

    let srcAccepting cg src direction = 
        srcAcceptingWithout cg src (fun _ -> false) direction

(*
    let simplePathSrcRepeat cg src canRepeat direction =
        let f = if direction = Up then neighborsIn else neighbors 
        let explored = ref 0
        let allNodes = cg.Graph.Vertices |> Set.ofSeq
        let cantReach = ref allNodes
        let rec search v seenLocs seenNodes = 
            explored := !explored + 1
            cantReach := Set.remove v !cantReach
            (* Stop if no unmarked node reachable without repeating location *)
            let exclude = (fun node -> node <> v && not (canRepeat node) && Set.contains node.Node.Loc seenLocs)
            let reachable = srcWithout cg v exclude Down
            let relevant = Set.exists (fun x -> Set.contains x reachable) !cantReach 
            if relevant then
                for u in f cg v do
                    let seenNodeBefore = Set.contains u seenNodes
                    let seenLocBefore = Set.contains u.Node.Loc seenLocations
                    let shouldContinue = not seenLocBefore || (canRepeat u && not seenNodeBefore)
                    if not (Set.contains u.Node.Loc seenLocs) then 
                        (* let newSeen = if canRepeat u then seen else (Set.add u.Node.Loc seen) *)
                        search u (Set.add u.Node.Loc seenLocs) (Set.add u seenNodes)
        search src Set.empty (Set.singleton cg.Start)
        Set.difference allNodes !cantReach

    let simplePathSrc cg src direction = 
        let canRepeat v = (v.Node.Typ = Topology.Unknown)
        simplePathSrcRepeat cg src canRepeat direction  *)

    let alongSimplePathSrcDstRepeat cg src dst canRepeat direction = 
        let f = if direction = Up then neighborsIn else neighbors
        let num_explored = ref 0
        let allNodes = cg.Graph.Vertices |> Set.ofSeq
        let cantReach = ref allNodes
        let rec search v seenLocations seenNodes =
            num_explored := !num_explored + 1
            if v = cg.End then 
                cantReach := Set.difference !cantReach seenNodes
            (* Stop if can't reach the end state *)
            let exclude = (fun node -> node <> v && not (canRepeat node) && Set.contains node.Node.Loc seenLocations)
            let reachable = srcWithout cg v exclude Down
            let seenUnmarked = not (Set.isEmpty (Set.intersect !cantReach seenNodes))
            let canReachUnmarked = Set.exists (fun v -> Set.contains v !cantReach) reachable
            if seenUnmarked || canReachUnmarked then
                let canReachDst = Set.contains dst reachable
                if canReachDst then
                    for u in f cg v do
                        let seenNodeBefore = Set.contains u seenNodes
                        let seenLocBefore = Set.contains u.Node.Loc seenLocations
                        let shouldContinue = not seenLocBefore || (canRepeat u && not seenNodeBefore)
                        if shouldContinue then
                            search u (Set.add u.Node.Loc seenLocations) (Set.add u seenNodes)
        search src Set.empty (Set.singleton cg.Start)
        Set.difference allNodes !cantReach

    let alongSimplePathSrcDst cg src dst direction = 
        let canRepeat v = (v.Node.Typ = Topology.Unknown)
        alongSimplePathSrcDstRepeat cg src dst canRepeat direction

    /// Checks if n1 in graph cg1 simulates n2 in cg2
    let supersetPaths (cg1, n1) (cg2, n2) : bool =
        let add k v map = 
            match Map.tryFind k map with 
            | None -> Map.add k (Set.singleton v) map
            | Some vs -> Map.add k (Set.add v vs) map

        let addAll k vs map = Set.fold (fun acc v -> add k v acc) map vs

        let merge x y = Map.fold (fun acc k v -> addAll k v acc) x y

        let remainsSuperset b = 
            Map.forall (fun k v -> 
                not (Map.exists (fun k' v' -> 
                    (k.Node.Loc = k'.Node.Loc && k <> k') &&
                    (Set.intersect v v' |> Set.isEmpty |> not) ) b)) b

        let stepNodeNode n1 n2 =
            logInfo3 (sprintf "Simulate: %s -- %s" (n1.ToString()) (n2.ToString())) 
            let neighbors1 = neighbors cg1 n1 |> Seq.filter isRealNode |> Set.ofSeq
            let neighbors2 = neighbors cg2 n2 |> Seq.filter isRealNode |> Set.ofSeq
            let nchars1 = Set.map (fun v -> v.Node.Loc) neighbors1
            let nchars2 = Set.map (fun v -> v.Node.Loc) neighbors2
            if not (Set.isSuperset nchars1 nchars2) then
                None
            else
                let newBisim = ref Map.empty
                let common = Set.intersect nchars1 nchars2 
                Set.iter (fun c ->
                    let v1 = Set.filter (fun v -> v.Node.Loc = c) neighbors1 |> Set.minElement
                    let v2 = Set.filter (fun v -> v.Node.Loc = c) neighbors2 |> Set.minElement
                    newBisim := add v1 v2 !newBisim
                ) common
                Some !newBisim
 
        let stepNodeNodes n1 n2s = 
            Set.fold (fun acc n2 ->
                match acc, stepNodeNode n1 n2 with 
                | None, _ | _, None -> None 
                | Some acc, Some x -> Some (merge acc x)
            ) (Some Map.empty) n2s

        let updateBisim b n1 n2s = 
            match b, stepNodeNodes n1 n2s with 
            | None, _ | _, None -> None
            | Some b, Some x -> Some (merge b x)

        let rec iter n bisim = 
            match n with 
            | 0 -> true
            | _ ->
                if Map.isEmpty bisim then true else
                if not (remainsSuperset bisim) then false else 
                let b = Map.fold updateBisim (Some Map.empty) bisim
                match b with 
                | None -> false
                | Some b -> iter (n-1) b

        if n1.Node.Loc <> n2.Node.Loc then false 
        else
            let bisim = Map.add n1 (Set.singleton n2) Map.empty
            let steps = max cg1.Graph.VertexCount cg2.Graph.VertexCount 
            iter steps bisim


module Minimize =

    type DominationSet = Map<CgState, Set<CgState>>

    let dominators (cg: T) root direction : DominationSet =
        let f = if direction = Up then neighbors else neighborsIn
        let dom = ref Map.empty
        let nodes = Reachable.dfs cg root direction
        let allNodes = cg.Graph.Vertices |> Set.ofSeq
        for n in allNodes do 
            dom := Map.add n allNodes !dom
        let mutable changed = true 
        while changed do 
            changed <- false 
            for n in nodes do
                let preds = seq {for p in (f cg n) do yield Map.find p !dom}
                let interAll = if Seq.isEmpty preds then Set.empty else Set.intersectMany preds
                let newSet = Set.union (Set.singleton n) interAll
                if (newSet <> Map.find n !dom) then 
                    dom := Map.add n newSet !dom 
                    changed <- true
        !dom

    let removeEdgesForDominatedNodes (cg: T) = 
        let dom = dominators cg cg.Start Down
        let domRev = dominators cg cg.End Up
        cg.Graph.RemoveEdgeIf (fun (e: TaggedEdge<CgState,unit>) -> 
            let ies = cg.Graph.OutEdges e.Target
            match Seq.tryFind (fun (ie: TaggedEdge<CgState,unit>) -> ie.Target = e.Source) ies with 
            | None -> false 
            | Some ie ->
                assert (ie.Source = e.Target)
                assert (ie.Target = e.Source)
                (Set.contains e.Target (Map.find e.Source dom) || Set.contains e.Source (Map.find e.Target domRev)) &&
                not (isUnknownRepeater cg e.Target || isUnknownRepeater cg e.Source)
        ) |> ignore

    let removeDeadEdgesHeuristic (cg: T) =
        cg.Graph.RemoveEdgeIf (fun (e: TaggedEdge<CgState,unit>) -> 
            let x = e.Target
            not (Reachable.srcDstWithout cg x cg.End (fun v -> v <> x && v.Node.Loc = e.Source.Node.Loc) Down)
        ) |> ignore

    let removeNodesThatCantReachEnd (cg: T) = 
        let canReach = Reachable.src cg cg.End Up
        cg.Graph.RemoveVertexIf(fun v -> 
            Topology.isTopoNode v.Node && not (Set.contains v canReach)
        ) |> ignore

    let removeNodesThatStartCantReach (cg: T) = 
        let canReach = Reachable.src cg cg.Start Down
        cg.Graph.RemoveVertexIf(fun v -> 
            Topology.isTopoNode v.Node && not (Set.contains v canReach)
        ) |> ignore

    (* let removeNodesNotReachableOnSimplePath (cg: T) =
        let canReach = Reachable.simplePathSrc cg cg.Start Down
        cg.Graph.RemoveVertexIf (fun v -> Topology.isTopoNode v.Node && not (Set.contains v canReach)) |> ignore *)

    let removeNodesNotOnAnySimplePathToEnd (cg: T) = 
        let canReach = Reachable.alongSimplePathSrcDst cg cg.Start cg.End Down
        cg.Graph.RemoveVertexIf (fun v -> Topology.isTopoNode v.Node && not (Set.contains v canReach)) |> ignore

    let delMissingSuffixPaths (cg: T) =
        let starting = neighbors cg cg.Start |> Seq.filter isRealNode |> Set.ofSeq
        cg.Graph.RemoveVertexIf (fun v -> 
            v.Node.Typ = Topology.InsideOriginates && 
            v.Accept.IsEmpty && 
            not (Set.contains v starting)
        ) |> ignore

    let absorbNeighbor cg x y = 
        for z in neighbors cg y do 
            cg.Graph.AddEdge (TaggedEdge(x,z,())) |> ignore
        cg.Graph.RemoveVertex y |> ignore

    let coalesceExternalNodes (cg: T) =
        let outStars = 
            cg.Graph.Vertices 
            |> Seq.filter (isUnknownRepeater cg)
        let toAddEdges = HashSet(HashIdentity.Structural)
        let toDelNodes = HashSet(HashIdentity.Structural)
        for os in outStars do
            let ons = neighbors cg os |> Set.ofSeq
            for x in ons do
                let xns = neighbors cg x |> Set.ofSeq
                if isRealNode x && x <> os && xns.Contains os then
                    toDelNodes.Add x |> ignore
                    for y in xns do
                        if not (ons.Contains y) then
                            toAddEdges.Add((os,y)) |> ignore
                    for y in neighborsIn cg x do
                        if not (ons.Contains y) then
                            toAddEdges.Add((y,os)) |> ignore
        for (x,y) in toAddEdges do
            cg.Graph.AddEdge (TaggedEdge(x,y,())) |> ignore
        for n in toDelNodes do 
            cg.Graph.RemoveVertex n |> ignore
           
    let minimizeO0 (cg: T) =
        removeNodesThatCantReachEnd cg
      
    let minimizeO1 (cg: T) =
        removeNodesThatCantReachEnd cg
        removeEdgesForDominatedNodes cg

    let minimizeO2 (cg: T) = 
        removeNodesThatCantReachEnd cg
        removeEdgesForDominatedNodes cg
        (* removeNodesNotReachableOnSimplePath cg *)
        removeDeadEdgesHeuristic cg

    let minimizeO3 (cg: T) =
        let count cg = cg.Graph.VertexCount + cg.Graph.EdgeCount
        logInfo1(sprintf "Node count: %d" cg.Graph.VertexCount)
        let prune () = 
            removeNodesThatCantReachEnd cg
            logInfo1(sprintf "Node count - after O1: %d" cg.Graph.VertexCount)
            removeEdgesForDominatedNodes cg
            (* removeNodesNotReachableOnSimplePath cg *)
            logInfo1(sprintf "Node count - after O2: %d" cg.Graph.VertexCount)

        let mutable sum = count cg
        prune() 
        while count cg <> sum do
            sum <- count cg
            prune ()
        removeNodesNotOnAnySimplePathToEnd cg
        removeDeadEdgesHeuristic cg
        coalesceExternalNodes cg
        logInfo1(sprintf "Node count - after O3: %d" cg.Graph.VertexCount)


module Consistency = 

    exception ConsistencyException of CgState * CgState
    type CounterExample =  CgState * CgState
    type Preferences = seq<CgState>
    type Ordering = Map<string, Preferences>
    type Constraints = BidirectionalGraph<CgState ,TaggedEdge<CgState,unit>>

    let simulate _ restrict (x,y) (i,j) =
        let restrict_i = copyGraph (Map.find i restrict)
        let restrict_j = copyGraph (Map.find j restrict)
        restrict_i.Graph.RemoveVertexIf (fun v -> v.Node.Loc = x.Node.Loc && v <> x) |> ignore
        restrict_j.Graph.RemoveVertexIf (fun v -> v.Node.Loc = y.Node.Loc && v <> y) |> ignore
        (* If x is not in the restricted graph for pref i, it does not subsume y *)
        if not (restrict_i.Graph.ContainsVertex x) then 
            false
        (* If y is not in the restricted graph for pref j, then we shouldn't consider this preference *)
        else if not (restrict_j.Graph.ContainsVertex y) then
            true
        else
            (* Remove nodes that appear 'above' for the more preferred, to avoid considering simple paths *)
            (* cheaper approximation of simple paths - can do better at cost of speed *)
            let exclude = (fun v -> v.Node.Loc = x.Node.Loc && v <> x)
            let reach = Reachable.srcWithout restrict_i x exclude Up |> Set.map (fun v -> v.Node.Loc)
            restrict_i.Graph.RemoveVertexIf (fun v -> v <> x && Set.contains v.Node.Loc reach) |> ignore
            (* Check if the more preferred simulates the less preferred *)
            Reachable.supersetPaths (restrict_i, x) (restrict_j, y) 

    let isPreferred f cg restrict (x,y) (reachX, reachY) =
        let subsumes i j =
            f cg restrict (x,y) (i,j)
        Set.forall (fun j -> 
            (Set.exists (fun i' -> i' <= j && subsumes i' j) reachX) ) reachY

    let checkIncomparableNodes (g: Constraints) edges = 
        for x in g.Vertices do
            for y in g.Vertices do
                if x <> y && not (Set.contains (x,y) edges || Set.contains (y,x) edges) then
                    raise (ConsistencyException(x,y))

    let removeUnconstrainedEdges (g: Constraints) edges =
        let both = Set.filter (fun (x,y) -> Set.exists (fun (a,b) -> x=b && y=a) edges) edges
        g.RemoveEdgeIf (fun e -> Set.contains (e.Source, e.Target) both) |> ignore

    let getOrdering (g: Constraints) edges =
        checkIncomparableNodes g edges
        removeUnconstrainedEdges g edges
        g.TopologicalSort ()

    let getReachabilityMap cg nodes = 
        let mutable reachMap = Map.empty
        for n in nodes do
            reachMap <- Map.add n (Reachable.srcAccepting cg n Down) reachMap
        reachMap

    let addPrefConstraints f cg (g: Constraints) r nodes reachMap =
        let mutable edges = Set.empty
        for x in nodes do
            for y in nodes do
                let reachX = Map.find x reachMap
                let reachY = Map.find y reachMap
                if x <> y && (isPreferred f cg r (x,y) (reachX,reachY)) then
                    logInfo1 (sprintf "%s is preferred to %s" (x.ToString()) (y.ToString()))
                    edges <- Set.add (x,y) edges
                    g.AddEdge (TaggedEdge(x, y, ())) |> ignore
                else if x <> y then
                    logInfo1 (sprintf "%s is NOT preferred to %s" (x.ToString()) (y.ToString()))
        g, edges

    let encodeConstraints f cg r nodes =
        let g = BidirectionalGraph<CgState ,TaggedEdge<CgState,unit>>()
        for n in nodes do 
            g.AddVertex n |> ignore
        let reachMap = getReachabilityMap cg nodes
        addPrefConstraints f cg g r nodes reachMap

    let findPrefAssignment f r cg nodes = 
        let g, edges = encodeConstraints f cg r nodes
        getOrdering g edges

    let addForLabel f r cg map l =
        (* If external, then no ordering required *)
        let (ain, _) = Topology.alphabet cg.Topo
        let ain = Set.map (fun (v: Topology.State) -> v.Loc) ain
        if ain.Contains l then 
            if not (Map.containsKey l map) then 
                let nodes = Seq.filter (fun v -> v.Node.Loc = l) cg.Graph.Vertices
                Map.add l (findPrefAssignment f r cg nodes) map
            else map
        else Map.add l Seq.empty map

    let failedGraph (cg: T) (failures: Topology.Failure.FailType list) : T =
        let failed = copyGraph cg
        let rec aux acc fs =
            let (vs,es) = acc 
            match fs with
            | [] -> acc
            | (Topology.Failure.NodeFailure s)::tl ->
                aux (s.Loc::vs, es) tl
            | (Topology.Failure.LinkFailure s)::tl ->
                aux (vs, (s.Source.Loc, s.Target.Loc)::(s.Target.Loc, s.Source.Loc)::es) tl
        let (failedNodes, failedEdges) = aux ([],[]) failures
        failed.Graph.RemoveVertexIf (fun v -> List.exists ((=) v.Node.Loc) failedNodes) |> ignore
        failed.Graph.RemoveEdgeIf (fun e -> List.exists ((=) (e.Source.Node.Loc, e.Target.Node.Loc)) failedEdges) |> ignore
        failed

    let restrictedGraphs cg prefs =
        let aux acc i =
            let r = restrict cg i 
            Minimize.removeNodesThatCantReachEnd r
            (* don't consider external ASes. Note: don't remove nodes after this *)
            r.Graph.RemoveVertexIf (fun v -> v.Node.Typ = Topology.Outside) |> ignore
            Map.add i r acc
        Set.fold aux Map.empty prefs

    let findOrdering f (cg: T) outName : Result<Ordering, CounterExample> =
        let prefs = preferences cg 
        let rs = restrictedGraphs cg prefs
        debug2 (fun () -> Map.iter (fun i g -> generatePNG g (outName + "-min-restricted" + string i)) rs)
        let labels = 
            cg.Graph.Vertices
            |> Seq.filter (fun v -> Topology.isTopoNode v.Node)
            |> Seq.map (fun v -> v.Node.Loc)
            |> Set.ofSeq 
        try Ok(Set.fold (addForLabel f rs cg) Map.empty labels)
        with ConsistencyException(x,y) ->
            Err((x,y) )

(*
    let enumerate n (cg: T) restrict (x,y) (i,j) = 
        let gx = Map.find i restrict
        let gy = Map.find j restrict 
        let failCombos = Topology.Failure.allFailures n cg.Topo
        Seq.forall (fun fails ->
            let failedX = failedGraph gx fails
            let failedY = failedGraph gy fails
            not((Set.contains x (Reachable.simplePathSrc failedX cg.Start Down)) &&
               (not ( Set.contains x (Reachable.alongSimplePathSrcDst failedX cg.Start cg.End Down))) &&
               (Set.contains y (Reachable.alongSimplePathSrcDst failedY cg.Start cg.End Down)))
        ) failCombos

    let findOrderingEnumerate n = findOrdering (enumerate n) *)

    let findOrderingConservative = findOrdering simulate


module ToRegex = 
    
    let constructRegex (cg: T) (state: CgState) : Regex.T =
        printfn "Starting state: %A" state
        let reMap = ref Map.empty

        let get v = 
            Common.Map.getOrDefault v Regex.empty !reMap

        let add k v = 
            reMap := Map.add k v !reMap

        let cgRev = copyReverseGraph cg
        (* populate the map *)
        for e in cgRev.Graph.Edges do
            printfn "adding: (%s,%s)" (e.Source.ToString()) (e.Target.ToString())
            add (e.Source, e.Target) (Regex.loc e.Source.Node.Loc)
        (* start with immediate neighbors *)
        let ns = 
            neighbors cgRev state
            |> Seq.filter isRealNode
        printfn "Immediate neighbors: %A" ns
        let queue = Queue()
        for n in ns do 
            queue.Enqueue n
        (* repeatedly look at the next neighbor *)
        while queue.Count > 0 do 
            let n = queue.Dequeue()
            printfn "looking at: %A" n
            let ms = neighbors cgRev n
            for m in ms do
                printfn "  3rd node: %A" m
                if m <> n then
                    let middleRe = get (n,n)
                    let labelSN = get (state,n)
                    let labelNS = get (n,state)
                    let labelNM = get (n,m)
                    let labelMN = get (m,n)
               
                    add (state,m) (Regex.concatAll [labelSN; Regex.star middleRe; labelNM])
                    add (state,state) (Regex.concatAll [labelSN; Regex.star middleRe; labelNS])
                    add (m,m) (Regex.concatAll [labelMN; Regex.star middleRe; labelNS])
                    add (m,state) (Regex.concatAll [labelMN; Regex.star middleRe; labelNS])
                    queue.Enqueue m
            cgRev.Graph.RemoveVertex n |> ignore
        
        let r1 = get (state, state)
        let r2 = get (state, cg.Start)
        let r3 = get (cg.Start, state)
        let r4 = get (cg.Start, cg.Start)
        let rloop = Regex.star (Regex.union r4 (Regex.concatAll [r3; Regex.star r1; r2]))
        Regex.concatAll [Regex.star r1; r2; rloop]
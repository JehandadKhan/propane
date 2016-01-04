﻿module Topology

open QuickGraph
open QuickGraph.Algorithms
open System.Collections.Generic

type NodeType = 
    | Start
    | End
    | Outside
    | Inside 
    | InsideOriginates
    | Unknown

type State = 
    {Loc: string; 
     Typ: NodeType}

type T = BidirectionalGraph<State,TaggedEdge<State,unit>>

exception InvalidTopologyException

(*
let setOriginators (topo: T) (orig: Set<string>) : T =
    let mutable conv = Map.empty
    let newTopo = BidirectionalGraph()
    for v in topo.Vertices do 
        if orig.Contains v.Loc then
            match v.Typ with 
            | Inside | InsideOriginates ->
                let nv = {Loc=v.Loc; Typ=InsideOriginates} 
                newTopo.AddVertex nv |> ignore
                conv <- Map.add v nv conv
            | _ ->
                failwith ("[Topology Error]: cannot set location: " + v.Loc + " to be internal")
        else 
            newTopo.AddVertex v |> ignore
            conv <- Map.add v v conv

    for e in topo.Edges do 
        let v = Map.find e.Source conv
        let u = Map.find e.Target conv
        newTopo.AddEdge (TaggedEdge<State,unit>(v,u,())) |> ignore
    newTopo *)

let copyTopology (topo: T) : T = 
    let newTopo = BidirectionalGraph<State,TaggedEdge<State,unit>>()
    for v in topo.Vertices do newTopo.AddVertex v |> ignore
    for e in topo.Edges do newTopo.AddEdge e |> ignore 
    newTopo

let alphabet (topo: T) : Set<State> * Set<State> = 
    let mutable ain = Set.empty 
    let mutable aout = Set.empty 
    for v in topo.Vertices do
        match v.Typ with 
        | Inside | InsideOriginates -> ain <- Set.add v ain
        | Outside | Unknown -> aout <- Set.add v aout
        | Start | End -> failwith "unreachable"
    (ain, aout)

let isTopoNode (t: State) = 
    match t.Typ with 
    | Start | End -> false
    | _ -> true

let isOutside (t: State) = 
    match t.Typ with 
    | Outside -> true
    | Unknown -> true
    | _ -> false

let isInside (t: State) = 
    match t.Typ with 
    | Inside -> true 
    | InsideOriginates ->  true 
    | _ -> false

let canOriginateTraffic (t: State) = 
    match t.Typ with 
    | InsideOriginates -> true 
    | Outside -> true
    | Unknown -> true
    | Inside -> false
    | Start | End -> false

let isPeer (topo: T) (state: State) =
    let receivesFromInside = 
        topo.InEdges state
        |> Seq.map (fun e -> e.Source)
        |> Seq.exists isInside
    (isOutside state) && receivesFromInside

let isWellFormed (topo: T) : bool =
    let onlyInside = copyTopology topo
    onlyInside.RemoveVertexIf (fun v -> isOutside v) |> ignore
    let d = Dictionary<State,int>()
    ignore (onlyInside.WeaklyConnectedComponents d)
    (Set.ofSeq d.Values).Count = 1

let rec addVertices (topo: T) (vs: State list) = 
    match vs with 
    | [] -> ()
    | v::vs -> 
        topo.AddVertex v |> ignore
        addVertices topo vs

let rec addEdgesUndirected (topo: T) (es: (State * State) list) = 
    match es with 
    | [] -> () 
    | (x,y)::es -> 
        topo.AddEdge (TaggedEdge(x,y,())) |> ignore
        topo.AddEdge (TaggedEdge(y,x,())) |> ignore
        addEdgesUndirected topo es

let rec addEdgesDirected (topo: T) (es: (State * State) list) = 
    match es with 
    | [] -> () 
    | (x,y)::es -> 
        topo.AddEdge (TaggedEdge(x,y,())) |> ignore
        addEdgesDirected topo es


module Failure =
    type FailType =
        | NodeFailure of State
        | LinkFailure of TaggedEdge<State,unit>
        
        override this.ToString() = 
            match this with 
            | NodeFailure n -> "Node(" + n.Loc + ")"
            | LinkFailure e -> "Link(" + e.Source.Loc + "," + e.Target.Loc + ")"
  
    let allFailures n (topo: T) : seq<FailType list> =
        let fvs = topo.Vertices |> Seq.filter isInside |> Seq.map NodeFailure
        let fes =
            topo.Edges
            |> Seq.filter (fun e -> isInside e.Source || isInside e.Target) 
            |> Seq.map LinkFailure 
        Seq.append fes fvs 
        |> Seq.toList
        |> Common.List.combinations n

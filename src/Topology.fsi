﻿module Topology

open QuickGraph
open System.Collections.Generic

type NodeType = 
   | Start
   | End
   | Outside
   | Inside
   | Unknown of Set<string>

[<Struct>]
type Node = 
   val Loc : string
   val Typ : NodeType
   new : string * NodeType -> Node

type T

/// Make a defensive copy of the topology
val copyTopology : T -> T
/// Build the internal and external alphabet from a topology
val alphabet : T -> Set<Node> * Set<Node>
//  Return the nodes in the topology
val vertices : T -> seq<Node>
//  Return all the edges in the topology
val edges : T -> seq<Node * Node>
//  Return all incoming edges for a given node
val inEdges : T -> Node -> seq<Node * Node>
//  Return all outgoing edges for a given node
val outEdges : T -> Node -> seq<Node * Node>
//  Return all the topological neighbors of a node
val neighbors : T -> Node -> seq<Node>
/// Check if a node is a valid topology node
val isTopoNode : Node -> bool
/// Check if a node represents an external location (external AS)
val isOutside : Node -> bool
/// Check if a node represents an internal location (under AS control)
val isInside : Node -> bool
/// Check if a node is the special 'out' node
val isUnknown : Node -> bool
/// Check if a node can originate traffic (e.g., TOR in DC)
val canOriginateTraffic : Node -> bool
/// Checks if a topology is well-formed. This involves checking 
/// for duplicate names, as well as checking that the inside is fully connected
val isWellFormed : T -> bool
/// Helper function for building topology
val addVertices : T -> Node list -> unit
/// Helper function for building topology
val addEdgesDirected : T -> (Node * Node) list -> unit
/// Helper function for building topology
val addEdgesUndirected : T -> (Node * Node) list -> unit
/// Find all the valid topology links corresponding to pairs of locations
val findLinks : T -> Set<string> * Set<string> -> (Node * Node) list
/// Find a topology node by location name
val findByLoc : T -> string -> Node option
/// Find peer topology nodes
val peers : T -> Node -> seq<Node>

type Kind = 
   | Concrete
   | Abstract
   | Template

type GraphInfo = 
   { Graph : T
     Pods : Map<string, Set<string>>
     InternalNames : Set<string>
     ExternalNames : Set<string>
     AsnMap : Map<string, int>
     RouterMap : Map<string, string>
     IpMap : Dictionary<string * string, string * string> }

type CustomLabel = 
   | SomeLabel of string 
   | AllLabel of string 
   | NameLabel of string

type EdgeInfo = 
   { Label : string
     OtherLabel : string option
     Source : string 
     Target : string 
     Scope : string
     Front : CustomLabel list 
     Back : CustomLabel list }

type EdgeLabelInfo = 
   Map<string*string, EdgeInfo list>

type TopoInfo = 
   class
      val NetworkAsn : int
      val Kind : Kind
      val ConcreteGraphInfo : GraphInfo
      val AbstractGraphInfo : GraphInfo
      val EdgeLabels : EdgeLabelInfo
      val NodeLabels : Map<string, string>
      val PodLabels : Set<string>
      val EnclosingScopes : Map<string, string list>
      val Concretization : Map<string, Set<string>>
      val Abstraction : Map<string, string>
      val Constraints : List<string>
      val EqConstraints : Set<string>
      new : int * Kind * GraphInfo * GraphInfo * EdgeLabelInfo * Map<string, string> * Set<string> * Map<string, string list> * Map<string, Set<string>> * Map<string, string> * List<string> * Set<string>
          -> TopoInfo
      member SelectGraphInfo : GraphInfo
      member IsTemplate : bool
   end

/// Get the string representation of the router using a given asn map
val routerMap : Map<string, string> -> string -> string
/// Get the string representation of the router
val router : string -> TopoInfo -> string
/// Read a topology from an XML file
val readTopology : string -> TopoInfo * Args.T

/// Examples of useful topologies for testing
module Examples = 
   type Tiers = Dictionary<Node, int>

   type Prefixes = Dictionary<Node, Route.Prefix>
   
   val topoDisconnected : unit -> T
   val topoDiamond : unit -> T
   val topoDatacenterSmall : unit -> T
   val topoDatacenterMedium : unit -> T
   val topoDatacenterMediumAggregation : unit -> T
   val topoDatacenterLarge : unit -> T
   val topoBadGadget : unit -> T
   val topoBrokenTriangle : unit -> T
   val topoBigDipper : unit -> T
   val topoSeesaw : unit -> T
   val topoStretchingManWAN : unit -> T
   val topoStretchingManWAN2 : unit -> T
   val topoPinCushionWAN : unit -> T
   val topoBackboneWAN : unit -> T
   val fatTree : int -> T * Prefixes * Tiers
   val complete : int -> T

/// Module with unit tests
module Test = 
   val run : unit -> unit
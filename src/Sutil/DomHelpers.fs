/// <summary>
/// Utilities to help with browser DOM API
/// </summary>
module Sutil.DomHelpers

open System
open Browser.Dom
open Browser.Types
open Browser.CssExtensions
open Interop

let log = Logging.log "dom"

let internal SvIdKey = "_svid"

let internal dispatch (target: EventTarget) name (data: obj) =
    if not (isNull target) then
        target.dispatchEvent (Interop.customEvent name data)
        |> ignore

let internal dispatchSimple (target: EventTarget) name = dispatch target name {|  |}

let internal dispatchCustom<'T> (target: EventTarget) (name: string) (init: CustomEventInit<'T>) =
    if not (isNull target) then
        target.dispatchEvent (customEvent name init)
        |> ignore

[<RequireQualifiedAccessAttribute>]
module internal NodeKey =
    let Disposables = "__sutil_disposables"
    let ResizeObserver = "__sutil_resizeObserver"
    let TickTask = "__sutil_tickTask"
    let Promise = "__sutil_promise"
    let NodeMap = "__sutil_nodes"
    let Groups = "__sutil_groups"
    let StyleClass = "__sutil_styleclass"

    let clear (node: Node) (key: string) = Interop.delete node key

    let get<'T> (node: Node) key : 'T option =
        let v: obj = Interop.get node key

        if isNull v then
            None
        else
            v :?> 'T |> Some

    let setUnlessExists<'T> (node: Node) key (value : 'T) : unit=
        if not (Interop.exists node key) then
            Interop.set node key value

    let getCreate<'T> (node: Node) key (cons: unit -> 'T) : 'T =
        match get node key with
        | Some v -> v
        | None ->
            let newVal = cons ()
            Interop.set node key newVal
            newVal


module internal Event =
    let NewStore = "sutil-new-store"
    let UpdateStore = "sutil-update-store"
    let ElementReady = "sutil-element-ready"
    let Mount = "sutil-mount"
    let Unmount = "sutil-unmount"
    let Show = "sutil-show"
    let Hide = "sutil-hide"
    let Updated = "sutil-updated"
    let Connected = "sutil-connected"
    //let NewStore = "sutil-new-store"
    //let DisposeStore = "sutil-dispose-store"

    let notifyEvent (doc: Document) name data =
        doc.dispatchEvent (Interop.customEvent name data)
        |> ignore

    let notifyUpdated doc =
        log ("notify document")
        notifyEvent doc Updated {|  |}


type internal CustomDispatch<'T> =
    | Detail of 'T option
    | Bubbles of bool
    | Composed of bool
    static member toCustomEvent<'T>(props: CustomDispatch<'T> list) =
        let mutable data: obj = upcast {|  |}

        for p in props do
            match p with
            | Detail d -> Interop.set data "detail" d
            | Bubbles b -> Interop.set data "bubbles" b
            | Composed c -> Interop.set data "composed" c

        data :?> CustomEventInit<'T>

    static member dispatch(target: EventTarget, name: string) =
        dispatchCustom<unit> target name (CustomDispatch.toCustomEvent<unit> ([]))

    static member dispatch(e: Event, name: string) =
        dispatchCustom<unit> (e.target) name (CustomDispatch.toCustomEvent<unit> ([]))

    static member dispatch<'T>(target: EventTarget, name: string, props: CustomDispatch<'T> list) =
        dispatchCustom<'T> target name (CustomDispatch.toCustomEvent<'T> props)

    static member dispatch<'T>(e: Event, name: string, props: CustomDispatch<'T> list) =
        dispatchCustom<'T> (e.target) name (CustomDispatch.toCustomEvent<'T> props)

let domId = Helpers.makeIdGenerator ()

[<Literal>]
let internal ElementNodeType = 1.0

[<Literal>]
let internal TextNodeType = 3.0

let isTextNode (n: Node) = n.nodeType = TextNodeType
let isElementNode (n: Node) = n.nodeType = ElementNodeType

let asTryElement (n: Node) =
    if isElementNode n then
        Some(n :?> HTMLElement)
    else
        None

let internal documentOf (n: Node) = n.ownerDocument

let internal applyIfElement (f: HTMLElement -> unit) (n: Node) =
    if isElementNode n then
        f (n :?> HTMLElement)


let internal getNodeMap (doc: Document) : obj =
    NodeKey.getCreate doc.body NodeKey.NodeMap (fun () -> upcast {|  |})

let internal setSvId (n: Node) id =
    let map = getNodeMap n.ownerDocument
    Interop.set map (string id) n
    Interop.set n SvIdKey id

    if (isElementNode n) then
        (n :?> HTMLElement)
            .setAttribute (SvIdKey, (string id))

let internal svId (n: Node) = Interop.get n SvIdKey

let internal hasSvId (n: Node) = Interop.exists n SvIdKey

let internal findNodeWithSvId (doc: Document) id : Node option =
    let map = getNodeMap doc
    let key = string id

    match Interop.exists map key with
    | true -> Some(Interop.get map key)
    | _ -> None

//let getId n =
//    let r = svId n
//    if (r = id) then Some n else None
//findNode doc.body getId

let internal rectStr (r: ClientRect) =
    $"{r.left},{r.top} -> {r.right},{r.bottom}"

let internal nodeStr (node: Node) =
    if isNull node then
        "null"
    else
        let mutable tc = node.textContent

        if tc.Length > 80 then
            tc <- tc.Substring(0, 80)

        match node.nodeType with
        | ElementNodeType ->
            let e = node :?> HTMLElement
            $"<{e.tagName.ToLower()}>#{svId node} \"{tc}\""
        | TextNodeType -> $"\"{tc}\"#{svId node}"
        | _ -> $"?'{tc}'#{svId node}"

let nodeStrShort (node: Node) =
    if isNull node then
        "null"
    else
        let mutable tc = node.textContent

        if tc.Length > 16 then
            tc <- tc.Substring(0, 16) + "..."

        match node.nodeType with
        | ElementNodeType ->
            let e = node :?> HTMLElement
            $"<{e.tagName.ToLower()}> #{svId node}"
        | TextNodeType -> $"text:\"{tc}\" #{svId node}"
        | _ -> $"?'{tc}'#{svId node}"

open Fable.Core.JsInterop
open Fable.Core



let internal children (node: Node) =
    let rec visit (child: Node) =
        seq {
            if not (isNull child) then
                yield child
                yield! visit child.nextSibling
        }

    visit node.firstChild

let rec internal descendants (node: Node) =
    seq {
        for child in children node do
            yield child
            yield! descendants child
    }

let rec internal descendantsDepthFirst (node: Node) =
    seq {
        for child in children node do
            yield! descendants child
            yield child
    }

let internal isSameNode (a: Node) (b: Node) =
    if isNull a then
        isNull b
    else
        a.isSameNode (b)

let private hasDisposables (node: Node) : bool = Interop.exists node NodeKey.Disposables

let internal getDisposables (node: Node) : IDisposable list =
    if hasDisposables node then
        Interop.get node NodeKey.Disposables
    else
        []

let private clearDisposables (node: Node) : unit = Interop.delete node NodeKey.Disposables

// Call all registered disposables on this node
let private cleanup (node: Node) : unit =
    let safeDispose (d: IDisposable) =
        try
            d.Dispose()
        with
        | x -> Logging.error $"Disposing {d}: {x} from {nodeStr node}"

    let d = getDisposables node
    log $"cleanup {nodeStr node} - {d.Length} disposable(s)"

    d |> List.iter safeDispose

    clearDisposables node

    dispatchSimple node Event.Unmount

let internal assertTrue condition message = if not condition then failwith message

let internal cleanupDeep (node: Node) : unit =
    descendantsDepthFirst node
    |> Array.ofSeq
    |> Array.iter cleanup

    cleanup node


module internal DomEdit =

    let log s =
        if Interop.exists window "domeditlog" then
            window?domeditlog (s)
        else
            Logging.log "dom" s

    let appendChild (parent: Node) (child: Node) =
        log $"appendChild parent='{nodeStrShort parent}' child='{nodeStrShort child}'"
        parent.appendChild (child) |> ignore
        log $"after: appendChild parent='{nodeStrShort parent}' child='{nodeStrShort child}'"

    let removeChild (parent: Node) (child: Node) =
        log $"removeChild parent='{nodeStrShort parent}' child='{nodeStrShort child}'"
        cleanupDeep child
        parent.removeChild (child) |> ignore
        log $"after: removeChild parent='{nodeStrShort parent}' child='{nodeStrShort child}'"

    let insertBefore (parent: Node) (child: Node) (refNode: Node) =
        log $"insertBefore parent='{nodeStrShort parent}' child='{nodeStrShort child}' refNode='{nodeStrShort refNode}'"
        parent.insertBefore (child, refNode) |> ignore

        log
            $"after: insertBefore parent='{nodeStrShort parent}' child='{nodeStrShort child}' refNode='{nodeStrShort refNode}'"

    let insertAfter (parent: Node) (newChild: Node) (refChild: Node) =
        let beforeChild =
            if isNull refChild then
                parent.firstChild
            else
                refChild.nextSibling

        insertBefore parent newChild beforeChild


// Cleanup all descendants and this node
// Remove node from parent
let internal unmount (node: Node) : unit =
    cleanupDeep node

    if not (isNull (node.parentNode)) then
        DomEdit.removeChild node.parentNode node

let internal clear (node: Node) =
    children node |> Array.ofSeq |> Array.iter unmount

let internal listen (event: string) (e: EventTarget) (fn: (Event -> unit)) : (unit -> unit) =
    e.addEventListener (event, fn)
    (fun () -> e.removeEventListener (event, fn) |> ignore)

let raf (f: float -> unit) =
    Window.requestAnimationFrame (fun t ->
        try
            f t
        with
        | x -> Logging.error $"raf: {x.Message}")

let rafu (f: unit -> unit) =
    Window.requestAnimationFrame (fun _ ->
        try
            f ()
        with
        | x -> Logging.error $"rafu: {x.Message}")
    |> ignore

let internal anyof (events: string list) (target: EventTarget) (fn: Event -> Unit) : unit =
    let rec inner e =
        events
        |> List.iter (fun e -> target.removeEventListener (e, inner))

        fn (e)

    events
    |> List.iter (fun e -> listen e target inner |> ignore)

let once (event: string) (target: EventTarget) (fn: Event -> Unit) : unit =
    let rec inner e =
        target.removeEventListener (event, inner)
        fn (e)

    listen event target inner |> ignore

let interval callback (delayMs: int) =
    let id =
        Fable.Core.JS.setInterval callback delayMs

    fun () -> Fable.Core.JS.clearInterval id

let timeout callback (delayMs: int) =
    let id =
        Fable.Core.JS.setTimeout callback delayMs

    fun () -> Fable.Core.JS.clearTimeout id


let rec internal forEachChild (parent: Node) (f: Node -> unit) =
    let mutable child = parent.firstChild

    while not (isNull child) do
        f child
        child <- child.nextSibling

let internal nodeIsConnected (node: Node) : bool = node?isConnected


module ClassHelpers =
    let splitBySpace (s: string) =
        s.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries)

    let setClass (className: string) (e: HTMLElement) = e.className <- className

    let toggleClass (className: string) (e: HTMLElement) =
        e.classList.toggle (className) |> ignore

    let addToClasslist classes (e: HTMLElement) =
        e.classList.add (classes |> splitBySpace)

    let removeFromClasslist classes (e: HTMLElement) =
        e.classList.remove (classes |> splitBySpace)

let internal nullToEmpty s = if isNull s then "" else s

let internal setAttribute (el: HTMLElement) (name: string) (value: obj) =
    let isBooleanAttribute name =
        (name = "hidden"
         || name = "disabled"
         || name = "readonly"
         || name = "required"
         || name = "checked")

    let svalue = string value

    if name = "class" then
        el |> ClassHelpers.addToClasslist svalue
    else if name = "class-" then
        el |> ClassHelpers.removeFromClasslist svalue
    else if isBooleanAttribute name then
        let bValue =
            if value :? bool then
                value :?> bool
            else
                svalue <> "false"
        // we'd call el.toggleAttribute( name, bValue) if it was available
        if bValue then
            el.setAttribute (name, "")
        else
            el.removeAttribute name

    else if name = "value" then
        Interop.set el "__value" value // raw value
        Interop.set el "value" svalue //
    else if name = "style+" then
        el.setAttribute ("style", (nullToEmpty (el.getAttribute ("style"))) + svalue)
    else
        el.setAttribute (name, svalue)


let private idSelector = sprintf "#%s"
let private classSelector = sprintf ".%s"
let private findElement (doc: Document) selector = doc.querySelector (selector)

let rec private visitChildren (parent: Node) (f: Node -> bool) =

    let mutable child = parent.firstChild

    while not (isNull child) do
        if f (child) then
            visitChildren (downcast child) f
            child <- child.nextSibling
        else
            child <- null

let rec private findNode<'T> (parent: Node) (f: Node -> 'T option) : 'T option =
    let mutable child = parent.firstChild
    let mutable result: 'T option = None

    while not (isNull child) do
        result <- f (child)

        if (result.IsNone) then
            result <- findNode child f

        child <-
            match result with
            | None -> child.nextSibling
            | Some x -> null

    result

let private prevSibling (node: Node) : Node =
    match node with
    | null -> null
    | _ -> node.previousSibling

let rec private lastSibling (node: Node) : Node =
    if (isNull node || isNull node.nextSibling) then
        node
    else
        lastSibling node.nextSibling

let private lastChild (node: Node) : Node = lastSibling (node.firstChild)

let rec private firstSiblingWhere (node: Node) (condition: Node -> bool) =
    if isNull node then
        null
    else if condition node then
        node
    else
        firstSiblingWhere (node.nextSibling) condition

let private firstChildWhere (node: Node) (condition: Node -> bool) =
    firstSiblingWhere node.firstChild condition

let rec private lastSiblingWhere (node: Node) (condition: Node -> bool) =
    if isNull node then
        null
    else if (condition node
             && (isNull node.nextSibling
                 || not (condition node.nextSibling))) then
        node
    else
        lastSiblingWhere node.nextSibling condition

let private lastChildWhere (node: Node) (condition: Node -> bool) =
    lastSiblingWhere node.firstChild condition

let rec internal visitElementChildren (parent: Node) (f: HTMLElement -> unit) =
    visitChildren parent (fun child ->
        if (child.nodeType = 1.0) then
            f (downcast child)

        true)


let internal addTransform (node: HTMLElement) (a: ClientRect) =
    let b = node.getBoundingClientRect ()

    if (a.left <> b.left || a.top <> b.top) then
        let s = Window.getComputedStyle (node)

        let transform =
            if s.transform = "none" then
                ""
            else
                s.transform

        node.style.transform <- sprintf "%s translate(%fpx, %fpx)" transform (a.left - b.left) (a.top - b.top)
        log node.style.transform

let internal fixPosition (node: HTMLElement) =
    let s = Window.getComputedStyle (node)

    if (s.position <> "absolute" && s.position <> "fixed") then
        log $"fixPosition {nodeStr node}"
        let width = s.width
        let height = s.height
        let a = node.getBoundingClientRect ()
        node.style.position <- "absolute"
        node.style.width <- width
        node.style.height <- height
        addTransform node a



let internal computedStyleOpacity e =
    try
        float (Window.getComputedStyle(e).opacity)
    with
    | _ ->
        log (sprintf "parse error: '%A'" (Window.getComputedStyle(e).opacity))
        1.0

let internal computedStyleTransform node =
    let style = Window.getComputedStyle (node)

    if style.transform = "none" then
        ""
    else
        style.transform

open Fable.Core.JS

///<summary>
/// Serialize tasks through an element. If the task already has a running task
/// wait for it to complete before starting the new task. Otherwise, run the
/// new task immediately
///</summary>
let internal wait (el: HTMLElement) (andThen: unit -> Promise<unit>) =
    let key = NodeKey.Promise
    let run () = andThen () |> Interop.set el key

    if Interop.exists el key then
        let p = Interop.get<Promise<unit>> el key
        Interop.delete el key
        p.``then`` run |> ignore
    else
        run ()


let internal textNode (doc: Document) value : Node =
    let id = domId ()
    log $"create \"{value}\" #{id}"
    let n = doc.createTextNode (value)
    setSvId n id
    upcast n

/// <summary>
/// The width of the browser viewport
/// </summary>
let viewportWidth () =
    Math.max (ifSetElse (document.documentElement.clientWidth, 0.0), ifSetElse (window.innerWidth, 0.0))

/// <summary>
/// The height of the browser viewport
/// </summary>
let viewportHeight () =
    Math.max (ifSetElse (document.documentElement.clientHeight, 0.0), ifSetElse (window.innerHeight, 0.0))

/// <exclude/>
type NodeListOf<'T> with
    /// Produce a seq<'T> from a NodeListOf<'T>. This is useful when working with document.querySelectorAll, for example
    member nodes.toSeq() =
        seq {
            for i in [ 0 .. nodes.length - 1 ] do
                yield nodes.[i]
        }


/// <exclude/>
type NodeList with
    /// Produce a seq from a NodeList. This is useful when working with document.querySelectorAll, for example
    member nodes.toSeq() =
        seq {
            for i in [ 0 .. nodes.length - 1 ] do
                yield nodes.[i]
        }

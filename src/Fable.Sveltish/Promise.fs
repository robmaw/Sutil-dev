namespace Sveltish

open System
open Fable.Core
open Fable.Core.JsInterop

[<AutoOpen>]
module ObservablePromise =

    type State<'T> =
        | Waiting
        | Result of 'T
        | Error of Exception

    type ObservablePromise<'T>() =
        let store = Store.make( Waiting )
        member _.Run (p : JS.Promise<'T>) =
                store <~ Waiting
                p |> Promise.map (fun v -> store <~ Result v)
                  |> Promise.catch (fun x -> store <~ Error x)
                  |> ignore
        interface IObservable<State<'T>> with
            member this.Subscribe(observer: IObserver<State<'T>>) = store.Subscribe(observer)


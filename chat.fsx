#I "./packages/Akka/lib/net45/"
#I "./packages/Newtonsoft.Json/lib/net45/"
#I "./packages/FsPickler/lib/net45/"
#I "./packages/FSPowerPack.Linq.Community/lib/net40/"
#I "./packages/Akka.FSharp/lib/net45/"
#I "./packages/Suave/lib/net40/"

#r "Suave.dll"
#r "Akka.dll"
#r "FsPickler.dll"
#r "FSharp.PowerPack.Linq.dll"
#r "Akka.FSharp.dll"
open Akka
open Akka.Actor
open Akka.FSharp
open System

module Chat = 
    open Akka
    open Akka.Actor
    open Akka.FSharp
    open System

    type RoomName = RoomName of string
    type UserName = UserName of string
    type Message = Message of string
    type Speak = (Message*UserName -> unit)

    type Notification = 
        | UserSaid of UserName*Message*RoomName
        
    type UserMessage = 
        | Say of Message*RoomName
        | Notify of Notification
        | CreateRoom of RoomName
        | JoinRoom of RoomName

    type RoomMessage = 
        | Join of UserName*Actor<UserMessage>
        | UserSays of Message*UserName       

    type ServerMessage = 
        | CreateRoom of RoomName*UserName*Actor<UserMessage>
        | JoinRoom of RoomName*UserName*Actor<UserMessage>
        | Connect of (UserName*(Notification->unit))
        
    type RoomActorState = {RoomName: RoomName; ConnectedUsers: Map<UserName,Actor<UserMessage>>}
    let roomActor initState (mailbox:Actor<RoomMessage>) =
        let rec loop state = actor {
            let! message = mailbox.Receive()
            match message with
            | UserSays (message, userName) ->
                let userActors = state.ConnectedUsers |> Map.toList |> List.map snd 
                let notification = (Notify (UserSaid (userName, message, (state.RoomName))))
                userActors |> List.iter (fun userActor -> userActor.Self <! notification)
                return! loop state
            | Join(userName, userActor) -> 
                return! loop {state with ConnectedUsers = state.ConnectedUsers |> Map.add userName userActor}
        } 
        loop initState
    
    let createRoom chatServer ((RoomName roomNameStr) as roomName) userName (userActor:Actor<UserMessage>) =
        let ra = roomActor {RoomName = roomName; ConnectedUsers = [(userName, userActor)] |> Map.ofList}
        spawn chatServer roomNameStr ra
        
    type UserActorState = {UserName: UserName; Rooms: Map<RoomName, IActorRef>}
    
    let userActor (chatServer:Actor<ServerMessage>) userName notificationFun (mailbox:Actor<UserMessage>) =
        let rec loop state = actor {
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match message with
                | UserMessage.JoinRoom roomName ->
                    let room = chatServer.Self <? ServerMessage.JoinRoom(roomName,state.UserName,mailbox) |> Async.RunSynchronously
                    return! loop {state with Rooms = state.Rooms |> Map.add roomName room}
                | Say(msg,roomName) ->
                    let roomActor = state.Rooms |> Map.find roomName
                    roomActor <! UserSays(msg,state.UserName)
                    return! loop state
                | Notify notification -> 
                    notificationFun notification
                    return! loop state
                | UserMessage.CreateRoom roomName ->
                    let room = chatServer.Self <? ServerMessage.CreateRoom(roomName, state.UserName, mailbox) |> Async.RunSynchronously
                    sender <! room
                    return! loop {state with Rooms = state.Rooms |> Map.add roomName room}
            }
        loop {UserName = userName; Rooms = Map.empty}
        
    let createUser chatServer ((UserName userNameStr) as userName) notficationFun =
        spawn chatServer (userNameStr) (userActor chatServer userName notficationFun)
    
    let chatServerActor (mailbox:Actor<ServerMessage>) = 
        let rec loop state = actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()
            match message with
            | Connect (userName, notificationFun) ->
                let user = createUser mailbox userName notificationFun
                sender <! user
                return! loop state
            | ServerMessage.CreateRoom (roomName, userName, userActor) ->
                let room = createRoom mailbox roomName userName userActor
                sender <! room
                return! loop (state |> Map.add roomName room)
            | ServerMessage.JoinRoom(roomName, userName, userActor) ->
                let room = state |> Map.find roomName
                room <! Join(userName, userActor)
                sender <! room
                return! loop state
        }
        loop Map.empty
    
    type Connection = {
        CreateRoom: RoomName -> unit
        JoinRoom: RoomName -> unit
        Say: (Message*RoomName) -> unit
    }
    
    type ChatServer = 
        {
            ServerActor: IActorRef
        }
    let createConnection chatServer userName notificationHandler = 
        let userActor = chatServer.ServerActor <? Connect(userName, notificationHandler) |> Async.RunSynchronously
        let createRoom roomName = userActor <? UserMessage.CreateRoom roomName |> Async.RunSynchronously
        let joinRoom roomName = userActor <? UserMessage.JoinRoom roomName |> Async.RunSynchronously
        let say message = userActor <? UserMessage.Say message |> Async.RunSynchronously
        {
            CreateRoom = createRoom
            JoinRoom = joinRoom
            Say = say
        }
        
    let createChatServer() = 
        let system = System.create "chat-system" (Configuration.load())
        { 
            ServerActor = spawn system "server" chatServerActor 
        }

open Chat
let giveItASpin() = 
    // Create stuff
    let chatServer = createChatServer()
    let userActor:IActorRef = chatServer.ServerActor <? Connect((UserName "Tomas"), (printfn "User Tomas received: %A")) |> Async.RunSynchronously
    printfn "User Tomas created"
    let userActor2:IActorRef = chatServer.ServerActor <? Connect((UserName "Tomas2"), (printfn "User Tomas2 received: %A")) |> Async.RunSynchronously
    printfn "User Tomas2 created"
    let roomActor:IActorRef = userActor <? UserMessage.CreateRoom (RoomName "Room1") |> Async.RunSynchronously
    printfn "Room created"
    userActor <! Say(Message "First message", RoomName "Room1")
    userActor2 <! UserMessage.JoinRoom(RoomName "Room1")
    System.Console.ReadLine() |> ignore
    userActor <! Say(Message "Second message", RoomName "Room1")
    userActor2 <! Say(Message "Third message", RoomName "Room1")
    System.Console.ReadLine() |> ignore

giveItASpin()
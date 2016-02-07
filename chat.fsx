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
        | RoomCreated of RoomName
        
    type UserMessage = 
        | Say of Message*RoomName
        | Notify of Notification
        | CreateRoom of RoomName
        | JoinRoom of RoomName
        | Reconnect of (Notification -> unit)

    type RoomMessage = 
        | Join of UserName*Actor<UserMessage>
        | UserSays of Message*UserName       

    type RoomMonitorMessage = 
        | CreateRoom of RoomName*UserName*Actor<UserMessage>
        | JoinRoom of RoomName*UserName*Actor<UserMessage>
        | GetRoomList
    
    type UserMonitorMessage = 
        | Connect of (UserName*(Notification->unit))
    
    type ChatServerMessage = 
        | Connect of (UserName*(Notification->unit))
        | GetRoomList
        
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
        
    type UserActorState = {UserName: UserName; Rooms: Map<RoomName, IActorRef>; NotificationFuns: (Notification->unit) list}
    
    let userActor userName (roomMonitor:IActorRef) notificationFun (mailbox:Actor<UserMessage>) =
        let rec loop state = actor {
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match message with
                | UserMessage.JoinRoom roomName ->
                    let room = roomMonitor <? RoomMonitorMessage.JoinRoom(roomName,state.UserName,mailbox) |> Async.RunSynchronously
                    return! loop {state with Rooms = state.Rooms |> Map.add roomName room}
                | Say(msg,roomName) ->
                    let roomActor = state.Rooms |> Map.find roomName
                    roomActor <! UserSays(msg,state.UserName)
                    return! loop state
                | Notify notification -> 
                    state.NotificationFuns |> List.iter (fun f -> f notification)
                    return! loop state
                | Reconnect notificationFun ->
                    return! loop {state with NotificationFuns = notificationFun::state.NotificationFuns}
                | UserMessage.CreateRoom roomName ->
                    match roomMonitor <? RoomMonitorMessage.CreateRoom(roomName, state.UserName, mailbox) |> Async.RunSynchronously with
                    | Some room -> 
                        printfn "Got a room"
                        sender <! Some room
                        return! loop {state with Rooms = state.Rooms |> Map.add roomName room}
                    | None ->
                        printfn "Didn't get a room"
                        sender <! None
                        return! loop state
            }
        loop {UserName = userName; Rooms = Map.empty; NotificationFuns = [notificationFun]}
        
    let createUser mailbox (roomMonitor:IActorRef) ((UserName userNameStr) as userName) notficationFun =
        spawn mailbox (userNameStr) (userActor userName roomMonitor notficationFun)
    
    type UserMonitorState = {UserMap: Map<UserName, IActorRef>}
    let userMonitorActor (roomMonitor:IActorRef) (mailbox:Actor<UserMonitorMessage>) = 
        let rec loop state = actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()
            match message with
            | UserMonitorMessage.Connect (userName, notificationFun) ->
                match state.UserMap |> Map.tryFind userName with
                | Some user ->
                    user <! Reconnect notificationFun
                    sender <! user
                    return! loop state
                | None ->
                    let user = createUser mailbox roomMonitor userName notificationFun
                    sender <! user
                    return! loop {state with UserMap = state.UserMap |> Map.add userName user}
        }
        loop {UserMap = Map.empty}
    
    let roomMonitorActor (mailbox:Actor<RoomMonitorMessage>) = 
        let rec loop state = actor {
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()
            match message with
            | RoomMonitorMessage.CreateRoom (roomName, userName, userActor) ->
                match state |> Map.containsKey roomName with
                | false ->
                    printfn "Room not exists"
                    let room = createRoom mailbox roomName userName userActor
                    sender <! Some room
                    let allUsers = select "akka://chat-system/user/server/user-monitor/*" mailbox.Context
                    allUsers <! (Notify (RoomCreated(roomName)))
                    return! loop (state |> Map.add roomName room)
                | true -> 
                    printfn "Room exists"
                    sender <! None
                    return! loop state
            | RoomMonitorMessage.JoinRoom(roomName, userName, userActor) ->
                let room = state |> Map.find roomName
                room <! Join(userName, userActor)
                sender <! room
                return! loop state
            | RoomMonitorMessage.GetRoomList ->
                printfn "Existing rooms: %A" state
                sender <! (state |> Map.toList |> List.map fst)
                return! loop state
        }
        loop Map.empty
    
    let chatServerActor (mailbox:Actor<ChatServerMessage>) = 
        let rec loop state = actor {
            let state' = 
                match state with
                | None -> 
                    let roomMonitor = spawn mailbox "room-monitor" roomMonitorActor
                    let userMonitor = spawn mailbox "user-monitor" (userMonitorActor roomMonitor)
                    Some (roomMonitor,userMonitor)
                | _ -> state
            let! message = mailbox.Receive()
            let sender = mailbox.Sender()
            match state', message with
            | Some (_, userMonitor), Connect(x,y) -> 
                let userConnection = userMonitor <? (UserMonitorMessage.Connect(x,y)) |> Async.RunSynchronously
                sender <! userConnection
            | Some (roomMonitor, _), GetRoomList -> 
                let roomList = roomMonitor <? (RoomMonitorMessage.GetRoomList) |> Async.RunSynchronously
                sender <! roomList
            | _ -> ()
            return! loop state'
        }
        loop None
    
    type Connection = {
        CreateRoom: RoomName -> unit
        JoinRoom: RoomName -> unit
        Say: (Message*RoomName) -> unit
        GetRoomList: unit -> RoomName list
    }
    
    type ChatServer = 
        {
            ServerActor: IActorRef
        }
    let createConnection chatServer userName notificationHandler = 
        let userActor = chatServer.ServerActor <? Connect(userName, notificationHandler) |> Async.RunSynchronously
        let createRoom roomName = 
            let result = userActor <? UserMessage.CreateRoom roomName |> Async.StartImmediate
            printfn "Create room result %A" result
        let joinRoom roomName = userActor <? UserMessage.JoinRoom roomName |> Async.RunSynchronously |> ignore
        let say message = userActor <? UserMessage.Say message |> Async.RunSynchronously
        let getRoomList() = chatServer.ServerActor <? GetRoomList |> Async.RunSynchronously
        {
            CreateRoom = createRoom
            JoinRoom = joinRoom
            Say = say
            GetRoomList = getRoomList
        }
        
    let createChatServer() = 
        let system = System.create "chat-system" (Configuration.load())
        { 
            ServerActor = spawn system "server" chatServerActor 
        }

open Chat
let giveItASpin() = 
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
    let roomActor:IActorRef = userActor2 <? UserMessage.CreateRoom (RoomName "Room2") |> Async.RunSynchronously
    userActor2 <! Say(Message "Third message", RoomName "Room2")
    System.Console.ReadLine() |> ignore

//giveItASpin()
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
        | UserJoinedRoom of RoomName*UserName
        | JoinedRoom of RoomName
        | RoomCreated of RoomName

    type UserMessage =
        | Say of Message*RoomName
        | Notify of Notification
        | JoinRoom of RoomName
        | Reconnect of (Notification -> unit)

    type UserConnection = {
        UserName: UserName
        Notify: Notification -> unit
    }

    type RoomMessage =
        | Join of UserConnection
        | UserSays of Message*UserName

    type RoomMonitorMessage =
        | CreateRoom of RoomName
        | JoinRoom of RoomName*UserConnection
        | GetRoomList

    type UserMonitorMessage =
        | Connect of (UserName*(Notification->unit))

    type ChatServerMessage =
        | Connect of (UserName*(Notification->unit))
        | CreateRoom of RoomName
        | GetRoomList

    type Room = {
        UserSays: Message -> UserName -> unit
    }

    type RoomMonitor = {
        CreateRoom: RoomName -> Room option
        JoinRoom: RoomName -> UserConnection -> Room
        GetRoomList: unit -> RoomName list
    }

    type RoomActorState = {RoomName: RoomName; ConnectedUsers: Map<UserName,UserConnection>}
    let roomActor initState (mailbox:Actor<RoomMessage>) =
        let notifyUsers state notification =
            let userActors = state.ConnectedUsers |> Map.toList |> List.map snd
            printfn "Users in room: %A" userActors
            userActors |> List.iter (fun userActor -> userActor.Notify notification)

        let rec loop state = actor {
            let! message = mailbox.Receive()
            match message with
            | UserSays (message, userName) ->
                notifyUsers state (UserSaid (userName, message, (state.RoomName)))
                return! loop state
            | Join(userConnection) ->
                printfn "In room joining %A" message
                notifyUsers state (UserJoinedRoom (state.RoomName, userConnection.UserName))
                printfn "Hello"
                return! loop {state with ConnectedUsers = state.ConnectedUsers |> Map.add userConnection.UserName userConnection}
        }
        loop initState

    let createRoom chatServer ((RoomName roomNameStr) as roomName) =
        let ra = roomActor {RoomName = roomName; ConnectedUsers = Map.empty}
        spawn chatServer roomNameStr ra

    type User = {
        UserName: UserName
        Say: Message -> RoomName -> unit
        JoinRoom: RoomName -> unit
        Notify: Notification -> unit
        Reconnect: (Notification->unit) -> unit
    }
    type UserActorState = {UserName: UserName; Rooms: Map<RoomName, Room>; NotificationFuns: (Notification->unit) list}
    let createUser owner (UserName userNameStr as userName) notificationFun (roomMonitor:RoomMonitor) =
        let userActor (mailbox:Actor<UserMessage>) =
            let notify notification = mailbox.Self <! Notify notification
            let rec loop state = actor {
                    let! message = mailbox.Receive()
                    let sender = mailbox.Sender()
                    match message with
                    | UserMessage.JoinRoom roomName ->
                        printfn "In user actor joining room"
                        let connection: UserConnection = {
                            UserName = state.UserName
                            Notify = notify
                        }
                        let room = roomMonitor.JoinRoom roomName connection
                        state.NotificationFuns |> List.iter (fun f -> f (JoinedRoom roomName))
                        return! loop {state with Rooms = state.Rooms |> Map.add roomName room}
                    | Say(msg,roomName) ->
                        let roomActor = state.Rooms |> Map.find roomName
                        roomActor.UserSays msg state.UserName
                        return! loop state
                    | Notify notification ->
                        printfn "notifying"
                        state.NotificationFuns |> List.iter (fun f -> f notification)
                        return! loop state
                    | Reconnect notificationFun ->
                        return! loop {state with NotificationFuns = notificationFun::state.NotificationFuns}
                }
            loop {UserName = userName; Rooms = Map.empty; NotificationFuns = [notificationFun]}
        let userActor = spawn owner userNameStr userActor
        {
            UserName = userName
            Say = (fun message roomName -> userActor <! Say(message, roomName))
            JoinRoom = (fun roomName -> userActor <! UserMessage.JoinRoom(roomName))
            Notify = (fun notification -> userActor <! Notify notification)
            Reconnect = (fun notificationFun -> userActor <! Reconnect notificationFun)
        }

    type UserMonitor = {
        Connect: UserName -> (Notification -> unit) -> User
    }
    type UserMonitorState = {UserMap: Map<UserName, User>}
    let createUserMonitor owner (roomMonitor:RoomMonitor) =
        let userMonitorActor (mailbox:Actor<UserMonitorMessage>) =
            let rec loop state = actor {
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match message with
                | UserMonitorMessage.Connect (userName, notificationFun) ->
                    match state.UserMap |> Map.tryFind userName with
                    | Some user ->
                        user.Reconnect notificationFun
                        sender <! user
                        return! loop state
                    | None ->
                        let user = createUser mailbox userName notificationFun roomMonitor
                        sender <! user
                        return! loop {state with UserMap = state.UserMap |> Map.add userName user}
            }
            loop {UserMap = Map.empty}

        let userMonitor = spawn owner "user-monitor" userMonitorActor
        {
            Connect = (fun userName notificationFun -> userMonitor <? Connect(userName, notificationFun) |> Async.RunSynchronously)
        }

    let createRoomMonitor owner =
        let roomMonitorActor (mailbox:Actor<RoomMonitorMessage>) =
            let rec loop state = actor {
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match message with
                | RoomMonitorMessage.CreateRoom (roomName) ->
                    match state |> Map.containsKey roomName with
                    | false ->
                        printfn "Room not exists"
                        let room = createRoom mailbox roomName
                        sender <! Some room
                        let allUsers = select "akka://chat-system/user/server/user-monitor/*" mailbox.Context
                        allUsers <! (Notify (RoomCreated(roomName)))
                        return! loop (state |> Map.add roomName room)
                    | true ->
                        printfn "Room exists"
                        sender <! None
                        return! loop state
                | RoomMonitorMessage.JoinRoom(roomName, user) ->
                    printfn "%A is joining room %A" user roomName
                    let room = state |> Map.find roomName
                    printfn "Found room %A" room
                    room <! RoomMessage.UserSays(Message "Hello", user.UserName)
                    printfn "did work? %A" message
                    room <! RoomMessage.Join(user)
                    printfn "Wow?"
                    sender <! room
                    return! loop state
                | RoomMonitorMessage.GetRoomList ->
                    printfn "Existing rooms: %A" state
                    sender <! (state |> Map.toList |> List.map fst)
                    return! loop state
            }
            loop Map.empty
        let roomMonitorActor = spawn owner "room-monitor" roomMonitorActor
        {
            CreateRoom = (fun roomName -> roomMonitorActor <? RoomMonitorMessage.CreateRoom(roomName) |> Async.RunSynchronously)
            JoinRoom = (fun roomName user -> roomMonitorActor <? RoomMonitorMessage.JoinRoom(roomName, user) |> Async.RunSynchronously)
            GetRoomList = (fun () -> roomMonitorActor <? RoomMonitorMessage.GetRoomList |> Async.RunSynchronously)
        }

    type ChatServer =
        {
            Connect: UserName -> (Notification-> unit) -> User
            CreateRoom: RoomName -> unit
            GetRoomList: unit -> RoomName list
        }
    let createChatServer() =
        let chatServerActor (mailbox:Actor<ChatServerMessage>) =
            let rec loop state = actor {
                let state' =
                    match state with
                    | None ->
                        let roomMonitor = createRoomMonitor mailbox
                        let userMonitor = createUserMonitor mailbox roomMonitor
    //                    let userMonitor = spawn mailbox "user-monitor" (userMonitorActor roomMonitor)
                        Some (roomMonitor,userMonitor)
                    | _ -> state
                let! message = mailbox.Receive()
                let sender = mailbox.Sender()
                match state', message with
                | Some (_, userMonitor), Connect(x,y) ->
                    let user = userMonitor.Connect x y
                    sender <! user
                | Some (roomMonitor, _), GetRoomList ->
                    let roomList = roomMonitor.GetRoomList()
                    sender <! roomList
                | Some (roomMonitor, _), CreateRoom roomName ->
                    roomMonitor.CreateRoom roomName |> ignore
                | _ -> ()
                return! loop state'
            }
            loop None

        let system = System.create "chat-system" (Configuration.load())
        let chatServerActor = spawn system "server" chatServerActor
        {
            Connect = (fun userName notificationFun -> chatServerActor <? ChatServerMessage.Connect(userName, notificationFun) |> Async.RunSynchronously)
            CreateRoom = (fun roomName -> chatServerActor <! ChatServerMessage.CreateRoom(roomName))
            GetRoomList = (fun () -> chatServerActor <? ChatServerMessage.GetRoomList |> Async.RunSynchronously)
        }

    type Connection = {
        CreateRoom: RoomName -> unit
        JoinRoom: RoomName -> unit
        Say: (Message*RoomName) -> unit
        GetRoomList: unit -> RoomName list
    }

    let createConnection chatServer userName notificationHandler =
        let user = chatServer.Connect userName notificationHandler
        let createRoom roomName =
            let result = chatServer.CreateRoom roomName
            printfn "Create room result %A" result
        let joinRoom roomName = user.JoinRoom roomName
        let say message = user.Say message
        let getRoomList() = chatServer.GetRoomList()
        {
            CreateRoom = createRoom
            JoinRoom = joinRoom
            Say = say
            GetRoomList = getRoomList
        }

open Chat
let giveItASpin() =
    let chatServer = createChatServer()
    let userActor:IActorRef = chatServer.ServerActor <? Connect((UserName "Tomas"), (printfn "User Tomas received: %A")) |> Async.RunSynchronously
    printfn "User Tomas created"
    let userActor2:IActorRef = chatServer.ServerActor <? Connect((UserName "Tomas2"), (printfn "User Tomas2 received: %A")) |> Async.RunSynchronously
    printfn "User Tomas2 created"
    let roomActor:IActorRef = chatServer.ServerActor <? ChatServerMessage.CreateRoom (RoomName "Room1") |> Async.RunSynchronously
    printfn "Room created"
    userActor <! Say(Message "First message", RoomName "Room1")
    userActor2 <! UserMessage.JoinRoom(RoomName "Room1")
    System.Console.ReadLine() |> ignore
    userActor <! Say(Message "Second message", RoomName "Room1")
    let roomActor:IActorRef = chatServer.ServerActor <? ChatServerMessage.CreateRoom (RoomName "Room2") |> Async.RunSynchronously
    userActor2 <! Say(Message "Third message", RoomName "Room2")
    System.Console.ReadLine() |> ignore

//giveItASpin()

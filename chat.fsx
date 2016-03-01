#load "references.fsx"
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

    type Room =
        {
            UserSays: Message -> UserName -> unit
            Join: UserConnection -> unit
        }

    type RoomMonitor = {
        CreateRoom: RoomName -> Room option
        JoinRoom: RoomName -> UserConnection -> Room
        GetRoomList: unit -> RoomName list
    }

    type RoomActorState = {RoomName: RoomName; ConnectedUsers: Map<UserName,UserConnection>}
    let roomActor initState (mailbox:Actor<RoomMessage>) =
        let notifyUsers state notification userName =
            let userActors =
                state.ConnectedUsers
                |> Map.toList
                |> List.map snd
            userActors |> List.iter (fun userActor -> userActor.Notify notification)

        let rec loop state = actor {
            let! message = mailbox.Receive()
            match message with
            | UserSays (message, userName) ->
                notifyUsers state (UserSaid (userName, message, (state.RoomName))) userName
                return! loop state
            | Join(userConnection) ->
                match state.ConnectedUsers |> Map.containsKey userConnection.UserName with
                | true -> ()
                | false ->
                    notifyUsers state (UserJoinedRoom (state.RoomName, userConnection.UserName)) userConnection.UserName
                return! loop {state with ConnectedUsers = state.ConnectedUsers |> Map.add userConnection.UserName userConnection}
        }
        loop initState

    let createRoom chatServer ((RoomName roomNameStr) as roomName) =
        let ra = roomActor {RoomName = roomName; ConnectedUsers = Map.empty}
        let roomActor = spawn chatServer roomNameStr ra
        {
            UserSays = (fun message userName -> roomActor <! UserSays(message, userName))
            Join = (fun userConnection -> roomActor <! Join(userConnection))
        }

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
                        let connection: UserConnection = {
                            UserName = state.UserName
                            Notify = notify
                        }
                        let room = roomMonitor.JoinRoom roomName connection
                        let state' = {state with Rooms = state.Rooms |> Map.add roomName room}
                        state.NotificationFuns |> List.iter (fun f -> f (JoinedRoom roomName))
                        return! loop state'
                    | Say(msg,roomName) ->
                        let roomActor = state.Rooms |> Map.find roomName
                        roomActor.UserSays msg state.UserName
                        return! loop state
                    | Notify notification ->
                        state.NotificationFuns |> List.iter (fun f -> f notification)
                        return! loop state
                    | Reconnect notificationFun ->
                        return! loop {state with NotificationFuns = notificationFun::state.NotificationFuns}
                }
            loop {UserName = userName; Rooms = Map.empty; NotificationFuns = [notificationFun]}

        let user = spawn owner userNameStr userActor
        {
            UserName = userName
            Say = (fun message roomName -> printfn "saying %A" (UserMessage.Say(message, roomName)); user <! UserMessage.Say(message, roomName); printfn "did I say something?")
            JoinRoom = (fun roomName -> user <! UserMessage.JoinRoom(roomName))
            Notify = (fun notification -> user <! Notify notification)
            Reconnect = (fun notificationFun -> user <! Reconnect notificationFun)
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
            Connect = (fun userName notificationFun -> userMonitor <? UserMonitorMessage.Connect(userName, notificationFun) |> Async.RunSynchronously)
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
                        let room = createRoom mailbox roomName
                        let allUsers = select "akka://chat-system/user/server/user-monitor/*" mailbox.Context
                        let state' = (state |> Map.add roomName room)
                        allUsers <! (Notify (RoomCreated(roomName)))
                        sender <! Some room
                        return! loop state'
                    | true ->
                        sender <! None
                        return! loop state
                | RoomMonitorMessage.JoinRoom(roomName, user) ->
                    let room = state |> Map.find roomName
                    room.Join user
                    sender <! room
                    return! loop state
                | RoomMonitorMessage.GetRoomList ->
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
        let createRoom roomName = chatServer.CreateRoom roomName
        let joinRoom roomName = user.JoinRoom roomName
        let say (message, roomName) = user.Say message roomName
        let getRoomList() = chatServer.GetRoomList()
        {
            CreateRoom = createRoom
            JoinRoom = joinRoom
            Say = say
            GetRoomList = getRoomList
        }

open Chat
let giveItASpin() =
    printfn "Started"
    let chatServer = createChatServer()
    printfn "Server created"
    let userActor = chatServer.Connect (UserName "Tomas") (printfn "User Tomas received: %A")
    let userActor2 = chatServer.Connect (UserName "Tomas2") (printfn "User Tomas2 received: %A")
    let roomActor = chatServer.CreateRoom (RoomName "Room1")
    printfn "Room created"
    userActor.JoinRoom (RoomName "Room1")
    userActor.Say (Message "First message") (RoomName "Room1")
    System.Console.ReadLine() |> ignore
    userActor.Say (Message "Second message") (RoomName "Room1")
    let roomActor = chatServer.CreateRoom (RoomName "Room2")
    userActor.JoinRoom (RoomName "Room2")
    userActor2.JoinRoom (RoomName "Room2")
    userActor2.Say (Message "Third message") (RoomName "Room2")
    System.Console.ReadLine() |> ignore

giveItASpin()

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

module Chat = 
    open Akka
    open Akka.Actor
    open Akka.FSharp
    open System

    type RoomName = RoomName of string
    type UserName = UserName of string
    type Message = Message of string

    type ChatMessage = 
        | CreateRoom of RoomName
        | JoinRoom of UserName*RoomName*(Message*UserName -> unit)
        | Reconnect of UserName*RoomName*(Message*UserName -> unit)
        | Chat of Message*UserName 
        | Notify of Message*UserName

    type Chat = 
        {
            CreateRoom: RoomName -> unit
            JoinRoom: RoomName -> UserName -> (Message*UserName -> unit) -> (Message -> unit)
            Reconnect: RoomName -> UserName -> (Message*UserName -> unit) -> (Message -> unit)
        }
    
    let createUserActor (room:Actor<_>) (UserName name) notifyFunc = 
        spawn room name
            (fun mailbox ->
                let rec loop notify = actor {
                    let! message = mailbox.Receive()
                    match message with
                    | Notify (msg,user) ->
//                        if user <> (UserName name)
                        notify (msg,user)
                        return! loop notify
                    | Chat (msg,user) ->
                        room.ActorSelection("*") <! Notify (msg,user)
                        return! loop notify
                    | Reconnect ((UserName userName),roomName,notify) ->
                        return! loop notify
                    return! loop notify
                }
                loop notifyFunc
            )
    
    let createRoom (chatServer:Actor<_>) (RoomName name) = 
        spawn chatServer name
            (fun mailbox ->
                let rec loop messages = actor {
                    let! message = mailbox.Receive()
                    let children = mailbox.ActorSelection("*")
                    
                    match message with
                    | JoinRoom (userName,roomName,notify) ->
                        let userActor = createUserActor mailbox userName notify
                        notify (Message """{Msg:"From actor"}""", UserName "tomas")
                        let say message = userActor <! Chat(message,userName)
                        let sender = mailbox.Sender()
                        sender <! say
                        return! loop messages
                    | Reconnect ((UserName userName),roomName,notify) ->
                        let userActor = mailbox.ActorSelection(userName)
                        userActor <! message
                        return! loop messages
                        
                    | _ -> return! loop messages
                }
                loop List.empty
            )

    let chatActor() =     
        let system = System.create "chat-system" (Configuration.load())
        spawn system "chat"
            (fun mailbox ->
                let rec loop() = actor {
                    let! message = mailbox.Receive()
                    let sender = mailbox.Sender()
                    match message with
                    | CreateRoom name ->
                        printfn "Creating room: %A" message
                        createRoom mailbox name |> ignore
                        return! loop()
                    | JoinRoom (userName,(RoomName name),notify) ->
                        let roomSelection = mailbox.ActorSelection(name)
                        let sender = mailbox.Sender()
                        let res = 
                            roomSelection <? JoinRoom (userName, (RoomName name), notify)
                            |> Async.RunSynchronously
                        sender <! res
                        return! loop()
                    | Reconnect (userName,(RoomName name),notify) ->
                        let roomSelection = mailbox.ActorSelection(name)
                        let sender = mailbox.Sender()
                        let res = 
                            roomSelection <? Reconnect (userName, (RoomName name), notify)
                            |> Async.RunSynchronously
                        sender <! res
                        return! loop()
                }
                loop())

    let createChat() = 
        let chatActor = chatActor()
        {
            CreateRoom = (fun n -> chatActor <! CreateRoom n)
            JoinRoom = (fun r u notify -> 
                            chatActor <? JoinRoom(u,r,notify)
                            |> Async.RunSynchronously)
            Reconnect = (fun r u notify -> 
                            chatActor <? Reconnect(u,r,notify)
                            |> Async.RunSynchronously)
        }
       
 
open Chat

let giveItASpin() = 
    let chat = createChat()
    chat.CreateRoom (RoomName "test")
    let say = chat.JoinRoom (RoomName "test") (UserName "tomas") (printfn "Tomas prompt: %A")
    say (Message "Tomas says: Hello world")
    say (Message "Tomas says: Hello world again")
    let say2 = chat.JoinRoom (RoomName "test") (UserName "tomas2") (printfn "Tomas2 prompt: %A")
    say2 (Message "Tomas2 says: Hello world1212")
    let say3 = chat.Reconnect (RoomName "test") (UserName "tomas") (printfn "Tomas reconnect prompt: %A")
    say2 (Message "Tomas2 says: Hello world1212")

//giveItASpin()
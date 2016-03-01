#load "references.fsx"
#load "chat.fsx"

open Akka
open Akka.FSharp
open Akka.Actor
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Suave.Sockets
open Suave.WebSocket
open System.IO
open System.Threading
open Chat

module SerializationHelpers =
    open Microsoft.FSharp.Reflection
    open System
    open Newtonsoft.Json
    open Newtonsoft.Json.Linq

    type SimpleDUSerializer() =
        inherit JsonConverter()
        override x.WriteJson(writer: JsonWriter, value: obj, serializer: JsonSerializer) =
            let union, fields =  FSharpValue.GetUnionFields(value, value.GetType())
            writer.WriteStartObject()
            writer.WritePropertyName("_type")
            writer.WriteValue(union.Name)
            writer.WritePropertyName("_data")
            serializer.Serialize(writer, fields.[0])
            writer.WriteEndObject()
        override x.ReadJson(reader: JsonReader, objectType: Type, existingValue: obj, serializer: JsonSerializer) =
            let jsonObject = JObject.Load(reader)
            let properties = jsonObject.Properties() |> List.ofSeq
            let duName = properties |> List.find (fun p -> p.Name = "_type") |> (fun p -> p.Value.Value<string>())
            match FSharpType.GetUnionCases objectType |> Array.filter (fun case -> case.Name = duName) with
            |[|case|] ->
                case.GetFields() |> Seq.iter (fun p -> printfn "Field: %A" p.DeclaringType)
                let fieldType = case.GetFields().[0].PropertyType
                let fieldValue =
                    properties
                    |> List.find (fun p -> p.Name = "_data")
                    |> (fun p ->
                        JsonConvert.DeserializeObject(p.Value.ToString(), fieldType))
                FSharpValue.MakeUnion(case,[|fieldValue|])
            |_ -> raise (exn "")
        override x.CanConvert(objectType: Type) =
            FSharpType.IsUnion(objectType)

let index() =
    printfn "Reading index"
    File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/index.html")
let file str = File.ReadAllText(__SOURCE_DIRECTORY__ + "/content/" + str)

type ContentType =
    | JS
    | CSS
    | JSX

let parseContentType = function
    | "js" -> Some JS
    | "css" -> Some CSS
    | "jsx" -> Some JSX
    | _ -> None

let serveContent (filePath,fileEnding) =
    request(fun _ ->
        printfn "File %A" (filePath,fileEnding)
        let contentType = fileEnding |> parseContentType
        let mimetype =
            match contentType with
            | Some JS -> "application/javascript"
            | Some CSS -> "text/css"
            | Some JSX -> "text/babel"
            | None -> raise (exn "Not supported content type")
        Writers.setMimeType mimetype
        >=> OK (file (filePath + "." + fileEnding))
    )

module API =
    open FSharp.Data
    open System
    open Chat
    open SerializationHelpers
    open Newtonsoft.Json
    open Microsoft.FSharp.Reflection

    type RoomCreated = {RoomName: string}
    type UserShort = {UserName: string}
    type RoomInfo = {RoomName: string; Users: UserShort list}
    type UserJoinedRoom = {UserName: string; RoomName: string}
    type JoinedRoom = {RoomName: string}
    type RoomShort = {RoomName: string}
    type RoomList = {Rooms: RoomShort list}
    type UserSaid = {RoomName: string; Message: string; UserName: string}

    [<JsonConverter(typeof<SimpleDUSerializer>)>]
    type Event =
        | RoomCreated of RoomCreated
        | RoomInfo of RoomInfo
        | UserJoinedRoom of UserJoinedRoom
        | JoinedRoom of JoinedRoom
        | RoomList of RoomList
        | UserSaid of UserSaid

    type Say = {Message: string; RoomName: string}
    type CreateRoom = {RoomName: string}
    type JoinRoom = {RoomName: string}

    [<JsonConverter(typeof<SimpleDUSerializer>)>]
    type Command =
        | Say of Say
        | CreateRoom of CreateRoom
        | JoinRoom of JoinRoom

    open Suave.Sockets.Control
    open Suave.Sockets.SocketOp
    let utf8Bytes (str:string) = System.Text.Encoding.UTF8.GetBytes(str)
    let utf8String (bytes:byte []) = System.Text.Encoding.UTF8.GetString(bytes)

    let sendTextOnSocket (socket: WebSocket) value =
        async {
            let text = JsonConvert.SerializeObject(value)
            let! x = socket.send Opcode.Text (utf8Bytes text) true
            match x with | _ -> return ()
        } |> Async.StartImmediate


    let connect (chat:Chat.ChatServer) userName (webSocket : WebSocket) cx =
        printfn "trying to shake hands: %A" userName
        let notificationHandler = function
            | Notification.UserSaid (UserName userName,Message message,RoomName roomName) ->
                UserSaid {Message = message; UserName = userName; RoomName = roomName}
                |> sendTextOnSocket webSocket
            | Notification.RoomCreated (RoomName name) ->
                RoomCreated {RoomName = name}
                |> sendTextOnSocket webSocket
            | Notification.UserJoinedRoom(RoomName roomName, UserName userName) ->
                UserJoinedRoom {UserName = userName; RoomName = roomName}
                |> sendTextOnSocket webSocket
            | _ -> ()

        let connection = Chat.createConnection chat userName notificationHandler
        let roomNames = connection.GetRoomList() |> List.map (fun (RoomName n) -> {RoomName = n}:RoomShort)
        RoomList {Rooms = roomNames}
        |> sendTextOnSocket webSocket

        printfn "Created connection, yey"
        socket {
            while true do
                let! inChoice = webSocket.read()
                match inChoice with
                | (Opcode.Text, bytes, true) ->
                    let msgStr = utf8String bytes
                    let command = JsonConvert.DeserializeObject<Command>(msgStr)

                    match command with
                    | Say command ->
                        connection.Say ((Chat.Message command.Message), RoomName command.RoomName)
                    | CreateRoom command ->
                        printfn "Creating room %A" command
                        connection.CreateRoom (RoomName command.RoomName)
                        printfn "Create room done"
                    | JoinRoom command ->
                        printfn "Joining room %A" command
                        connection.JoinRoom (RoomName command.RoomName)
                        printfn "Joined room"

                    printfn "Parsed %A" msgStr
                | _ -> ()
        }

    let app (chat:Chat.ChatServer) =
        choose
            [
                pathScan "/_socket/connect/%s" (fun (userName) ->
                    handShake (connect chat (UserName userName)))
//                POST >=> pathScan "/api/room/%s" (fun name -> chat.CreateRoom (Chat.RoomName name); OK ("Created " + name))
//                POST >=> pathScan "/api/room/%s/join" connect
                // GET >=> pathScan "/api/room/%s/users" getUsers
                // pathScan "/api/room/%s/join/%s" (fun (roomName,userName) -> handShake (joinRoom chat roomName userName))
            ]

let serveIndex : WebPart =
    request (fun r ->
        let s = index()
        OK s
    )

let content =
    choose
        [
            path "/" >=> Writers.setMimeType "text/html" >=> serveIndex
            pathScan "/%s.%s" serveContent
        ]

let app =
    choose
        [
            path "/favicon.ico" >=> NOT_FOUND "No favicon"
            (API.app (Chat.createChatServer()))
            content
            NOT_FOUND "Uhoh"
        ]

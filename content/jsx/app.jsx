// Action types
const LOGIN = "LOGIN";
const LOGIN_RESULT = "LOGIN_RESULT";
const CREATE_ROOM = "CREATE_ROOM";
const ROOM_CREATED = "ROOM_CREATED";
const JOIN_ROOM = "JOIN_ROOM";
const JOINED_ROOM = "JOINED_ROOM";
const USER_JOINED = "USER_JOINED";
const SEND_MESSAGE = "SEND_MESSAGE";
const MESSAGE_RECIEVED = "MESSAGE_RECIEVED";

// Login status
const FAILED = "FAILED";
const IN_PROGRESS = "IN_PROGRESS";
const OK = "OK";
const NOT_STARTED = "NOT_STARTED";
const ROOM_LIST = "ROOM_LIST";

// Action creators
const loginRequested = (userName) => {
    return {
        type: LOGIN,
        status: IN_PROGRESS,
        userName
    }
}

const loginSuccess = (userName, connection) => {
    return {
        type: LOGIN_RESULT,
        status: OK,
        userName,
        connection
    }
}

const roomCreated = (roomName) => {
    return {
        type: ROOM_CREATED,
        roomName
    }
}

const roomList = rooms => {
    return {
        type: ROOM_LIST,
        rooms
    }
}

const createRoom = (roomName, connection) => {
    console.log("Creating room: " + roomName)
    console.log(roomName)
    console.log(connection)
    return dispatch => {
        connection.createRoom(roomName)
    }
};

const joinRoom = (roomName, connection) => {
    return dispatch => {
        connection.joinRoom(roomName)
    }
}

const sendMessage = (message) => {
    return {
        type: SEND_MESSAGE,
        message
    };
};

const messageReceived = (userName, message) => {
    return {
        type: MESSAGE_RECIEVED,
        userName,
        message
    }
}

const userJoined = (userName, roomName) => {
    return {
        type: USER_JOINED,
        userName,
        roomName
    }
}

const joinedRoom = (roomName) => {
    return {
        type: JOINED_ROOM,
        roomName
    }
}

function connectToServer(userName) {
    return dispatch => {
        console.log("connecting to server for user: " + userName)
        console.log("handling connect request")
        var root = "ws://" + window.location.hostname;
        if (window.location.port != "") root = root + ":" + window.location.port;
        root = root + "/";

        var websocket = new WebSocket(root + "_socket/connect/" + userName);

        var say = function(text, roomName) {
            var msg = {"_type":"Say", "_data": {"Message":text, "RoomName": roomName}};
            var messageString = JSON.stringify(msg);
            websocket.send(messageString);
        };

        var createRoom = function(roomName) {
            console.log("Sending create room to server: " + roomName)
            var messageString = JSON.stringify({"_type": "CreateRoom", "_data": {"RoomName": roomName}});
            websocket.send(messageString);
        };

        const joinRoom = roomName => {
            console.log("Joining room: " + roomName)
            var messageString = JSON.stringify({"_type": "JoinRoom", "_data": {"RoomName": roomName}});
            websocket.send(messageString);
            dispatch(joinedRoom(roomName))
        }

        websocket.onmessage = function (event) {
            console.log("Received some data:");
            console.log(event.data);
            var data = JSON.parse(event.data);
            switch(data["_type"]) {
                case "RoomCreated":
                    dispatch(roomCreated(data["_data"].RoomName))
                    break
                case "RoomList":
                    dispatch(roomList(data["_data"].Rooms))
                    break
                case "UserJoinedRoom":
                    dispatch(userJoined(data["_data"].UserName, data["_data"].RoomName))
            }
        };

        websocket.onopen = function() {
            var connection = {
                say,
                createRoom,
                joinRoom
            };
            console.log("connected to server for user: " + userName)
            // connection.createRoom("Room1");
            // connection.say("Hello room1", "Room1");
            dispatch(loginSuccess(userName, connection));
        };
    }
}

// Reducers
const header = (state = {
    login: NOT_STARTED,
    userName: undefined
},action) => {
    console.log("In header action: ");
    console.log(action);
    console.log("In header state: ");
    console.log(state);
    switch(action.type) {
        case LOGIN:
            return Object.assign({}, state, {
                login: IN_PROGRESS
            });
        case LOGIN_RESULT:
            if(action.status == OK) {
                return Object.assign({}, state, {
                    userName: action.userName,
                    login: OK
                });
            }
        default:
            return state;
    }
};

const roomView = (state = {
    roomList: []
}, action) => {
    console.log("RoomView state: ")
    console.log(state)
    console.log(action)
    switch(action.type) {
        case ROOM_CREATED:
            console.log("RoomView new state: ")
            var newState = Object.assign({}, state, {
                roomList: [...state.roomList, action.roomName]
            });
            console.log("RoomView new state: ")
            console.log(newState)
            return newState
        case ROOM_LIST:
            var rooms = action.rooms || []
            return Object.assign({}, state, {
                roomList: rooms.map(r => r.RoomName)
            })
        default:
            return state;
    }
};

const roomMessages = (state = {
    activeRoom: "",
    roomList: {}
}, action) => {
    switch(action.type) {
        case USER_JOINED:
            let who = "Server"
            let message = action.userName + " joined the room"
            let roomMessages = state[action.roomName]
            var newMessages = []
            if(roomMessages) {
                newMessages = [...roomMessages, {userName: who, message}]
            }
            else {
                newMessages = [{userName: who, message}]
            }
            let roomList = Object.assign({}, state.roomList, {})
            roomList[action.roomName] = newMessages
            let newState = Object.assign({}, state, {roomList: roomList})
            return newState
        case JOINED_ROOM:
            return Object.assign({}, state, {
                activeRoom: action.roomName
            })
        default:
            return state;
    }
}

const NOT_CONNECTED = "NOT_CONNECTED";
const CONNECTED = "CONNECTED";
const connection = (state = {
    connectionStatus: NOT_CONNECTED,
    connection: undefined,
    userName: undefined
}, action) => {
    switch(action.type) {
        case LOGIN_RESULT:
            if(action.status == OK) {
                return Object.assign({}, state, {
                    connection: action.connection,
                    connectionStatus: CONNECTED,
                    userName: action.userName
                });
            }
        default:
            return state;
    }
}

const {Component} = React;
const {createStore, combineReducers, applyMiddleware} = Redux;
const {connect,Provider} = ReactRedux;
const {render} = ReactDOM;

const chatApp = combineReducers({
    header,
    roomView,
    connection,
    roomMessages
});


// Components
class HeaderView extends Component {
    render() {
        const {loginClick, userName, connectionStatus} = this.props;

        this.handleClick = function(e) {
            loginClick(this.refs.userName.value);
        }

        var loginBar;
        if(userName) {
            loginBar = <span>Welcome {userName}</span>
        } else if(connectionStatus == IN_PROGRESS) {
            loginBar = <span>Please hold on</span>
        }
        else {
            loginBar = <span>Please login now<input type="text" ref="userName"/><button onClick={(e) => this.handleClick(e)} >Please login</button></span>
        }

        return (
            <div>
                {loginBar}
            </div>
        );
    }
}

const mapStateToHeaderProps = (state) => {
    return {
        userName: state.connection.userName,
        connectionStatus: state.connection.connectionStatus
    }
}

const mapDispatchToProps = (dispatch) => {
    return {
        loginClick: (userName) => dispatch(connectToServer(userName))
    }
};

const Header = connect(
    mapStateToHeaderProps,
    mapDispatchToProps
)(HeaderView);

const Room = ({roomName, onClick}) => (
    <li onClick={onClick}>
        {roomName}
    </li>
)

const RoomList = ({rooms, onRoomClick}) => (
    <div>
        <h2>Existing rooms</h2>
        <ul className="room-list">
            {rooms.map(room =>
                <Room
                    key={room}
                    roomName={room}
                    onClick={() => onRoomClick(room)}
                />
            )}
        </ul>
    </div>
)

const mapStateToRoomListProps = (state) => {
    return {
        connection: state.connection.connection,
        rooms: state.roomView.roomList
    }
}

const mapDispatchToRoomListProps = (dispatch) => {
    return {
        onRoomClick: (roomName, connection) => {
            dispatch(joinRoom(roomName, connection))
        }
    }
}

const mergeToRoomListProps = (stateProps, dispatchProps, ownProps) => {
    var mergedProps = {
        onRoomClick: roomName => dispatchProps.onRoomClick(roomName, stateProps.connection)
    }
    return Object.assign({}, ownProps, stateProps, dispatchProps, mergedProps)
}

const RoomListContainer = connect(
    mapStateToRoomListProps,
    mapDispatchToRoomListProps,
    mergeToRoomListProps
)(RoomList);

const mapStateToCreateRoomProps = (state) => {
    return {
        connection: state.connection.connection
    }
}

const mapDispatchToCreateRoomProps = (dispatch, ownProps) => {
    return {
        onClick: (roomName, connection) => dispatch(createRoom(roomName, connection))
    }
}

const mergeCreateRoomProps = (stateProps, dispatchProps, ownProps) => {
    var mergedProps = {
        onClick: roomName => dispatchProps.onClick(roomName, stateProps.connection)
    }
    return Object.assign({}, ownProps, stateProps, dispatchProps, mergedProps)
}

class CreateRoom extends Component {
    render() {
        const {onClick} = this.props;
        this.handleClick = (e) => {
            onClick(this.refs.roomName.value)
        }
        return (
            <div>
                <h3>Create room</h3>
                <input type="text" ref="roomName" ></input>
                <button name="createRoom" onClick={(e) => this.handleClick(e)}>Create room</button>
            </div>
        )
    }
}

const CreateRoomContainer = connect(
    mapStateToCreateRoomProps,
    mapDispatchToCreateRoomProps,
    mergeCreateRoomProps
)(CreateRoom)

const ChatLogEntry = ({message}) =>
    <div className="message-entry">
        <span className="user-name">{message.userName}</span>
        <div className="message">{message.message}</div>
    </div>


const ChatLogContainer = ({messages}) =>
    <div>
        {messages.map(message =>
            <ChatLogEntry
                key={message.message}
                message={message}
            />
        )}
    </div>

const MessagePanelContainer = () =>
    <div className="message-panel">
        <input type="text"></input>
    </div>


const ChatWindow = ({roomMessages}) => {
    let activeMessages = roomMessages.roomList[roomMessages.activeRoom] || []
    return (
        <div>
            <ChatLogContainer messages={activeMessages}/>
            <MessagePanelContainer />
        </div>
    )
}

const mapStateToChatWindowContainerProps = (state) => {
    console.log("State in window container map state")
    console.log(state)
    return {
        roomMessages: state.roomMessages
    }
}

const ChatWindowContainer = connect(
    mapStateToChatWindowContainerProps
)(ChatWindow)

const LoggedInContainer = () =>
    <div>
        <RoomListContainer />
        <CreateRoomContainer />
        <ChatWindowContainer />
    </div>

const ChatApp = ({connection}) => {
    if(connection.connectionStatus == CONNECTED){
        return (
            <LoggedInContainer />
        )
    }
    else {
        return (
            <Header />
        )
    }
}

const mapStateToChatAppProps = (state) => {
    return {
        connection: state.connection
    }
}

const ChatAppContainer = connect(
    mapStateToChatAppProps
)(ChatApp)

const thunkMiddleware = ({ dispatch, getState }) => {
  return next => action =>
    typeof action === 'function' ?
      action(dispatch, getState) :
      next(action);
}

function configureStore(initState) {
    return applyMiddleware(thunkMiddleware)(createStore)(chatApp, initState);
}
const store = configureStore();

const rootElement = document.getElementById('app')
render(
  <Provider store={store}>
    <ChatAppContainer />
  </Provider>,
  rootElement
)

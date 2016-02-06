// Action types
const LOGIN = "LOGIN";
const LOGIN_RESULT = "LOGIN_RESULT";
const CREATE_ROOM = "CREATE_ROOM";
const ROOM_CREATED = "ROOM_CREATED";
const JOIN_ROOM = "JOIN_ROOM";
const USER_JOINED = "USER_JOINED";
const SEND_MESSAGE = "SEND_MESSAGE";
const MESSAGE_RECIEVED = "MESSAGE_RECIEVED";

// Login status
const FAILED = "FAILED";
const IN_PROGRESS = "IN_PROGRESS";
const OK = "OK";
const NOT_STARTED = "NOT_STARTED";

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

function connectToServer(userName) {
    return dispatch => {
        console.log("connecting to server for user: " + userName)
        console.log("handling connect request")
        var root = "ws://" + window.location.hostname;
        if (window.location.port != "") root = root + ":" + window.location.port;
        root = root + "/";
    
        var websocket = new WebSocket(root + "_socket/connect/" + userName);
        
        var say = function(text, roomName) {
            var msg = {"_type":"Say", "Message":text, "RoomName": roomName};
            var messageString = JSON.stringify(msg);
            websocket.send(messageString);
        };

        var createRoom = function(roomName) {
            console.log("Sending create room to server: " + roomName)
            var messageString = JSON.stringify({"_type": "CreateRoom", "RoomName": roomName});
            websocket.send(messageString);
        };
        
        websocket.onmessage = function (event) {
            console.log("Received some data:");
            console.log(event.data);
            var data = JSON.parse(event.data);
            switch(data["_type"]) {
                case "RoomCreated": 
                    dispatch(roomCreated(data.RoomName))
            }
        };
        
        websocket.onopen = function() {
            var connection = {
                say,
                createRoom
            };
            console.log("connected to server for user: " + userName)
            // connection.createRoom("Room1");
            // connection.say("Hello room1", "Room1");
            dispatch(loginSuccess(userName, connection));
        };
    }
}

const createRoom = (roomName, connection) => {
    console.log("Creating room: " + roomName)
    console.log(roomName)
    console.log(connection)
    return dispatch => {
        connection.createRoom(roomName)
    }
//    return {
//        type: CREATE_ROOM,
//        userName,
//        roomName
//    };
};

const joinRoom = (userName, roomName) => {
    return {
        type: JOIN_ROOM,
        userName,
        roomName
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

const doStuff = (ds, userName) => {
    if(ds) {
        ds(userName);
    }
    console.log("Clicked: " + userName);
    const something = (name) => {console.log("From action something " + userName + " " + name)};
    return {
        type: "DO_STUFF",
        userName,
        doStuff: something
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
    switch(action.type) {
        case ROOM_CREATED:
            return Object.assign({}, state, {
                roomList: [...state.roomList, action.roomName]
            });
        default:
            return state;
    }    
};

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
    connection
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
            loginBar = <span>Please login <input type="text" ref="userName"/><button onClick={(e) => this.handleClick(e)} >Please login</button></span>
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
        createRoomClick: (roomName) => {
            state.connection.connection.createRoom(roomName)
        },
        onRoomClick: (roomName) => {
            console.log("Clicked room: " + roomName);
        },
        rooms: state.roomView.roomList
    }
}

const mapDispatchToRoomListProps = (dispatch) => {
    return {
    }
}

const RoomListContainer = connect(
    mapStateToRoomListProps,
    mapDispatchToRoomListProps
)(RoomList);

const mapStateToCreateRoomProps = (state) => {
    console.log("State is")
    console.log(state)
    return {
        connection: state.connection.connection
    }
}

const mapDispatchToCreateRoomProps = (dispatch, ownProps) => {
    console.log("Own props")
    console.log(ownProps)
    console.log(dispatch)
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
                <button name="createRoom" onClick={(e) => this.handleClick(e)}>"Create room"</button>
            </div>
        )
    }
}

const CreateRoomContainer = connect(
    mapStateToCreateRoomProps,
    mapDispatchToCreateRoomProps,
    mergeCreateRoomProps
)(CreateRoom)

const ChatApp = ({connection}) => {
    if(connection.connectionStatus == CONNECTED){
        return (
            <div>
                <RoomListContainer />
                <CreateRoomContainer />
            </div>
        )
    }
    else {
        return (
            <div>
                <Header />
            </div>
        )
    }
}

const mapStateToChatAppProps = (state) => {
    console.log("The state is:")
    console.log(state)
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

// const room = (state  = [], action) => {
//     switch(action.type) {
//         case ROOM_CREATED:
//                         
//     }
// };

// var Header = React.createClass({
//   render: function() {
//     return (
//       <div className="header">
//         This is the header
//       </div>
//     );
//   }
//// });
//
//var Chat = React.createClass({
//    render: function() {
//        return(
//            <div className="chat-window">
//            This is a chat window
//            </div>
//        );
//    }    
//})
//
//var UserList = React.createClass({
//    render: function() {
//        return(
//            <div className="user-list">
//            This is the user list
//            </div>
//        );
//    }    
//})
//
//var ChatRoom = React.createClass({
//    render: function() {
//        return(
//            <div className="chat-room">
//                <Chat />
//                <UserList />
//            </div>
//        );
//    }
//})
//
//var ChatContainer = React.createClass({
//    render: function() {
//        return (
//            <div className="chat-container">
//                <aside>
//                    <RoomListView />
//                </aside>
//                <Chat />
//            </div>
//        );
//    }
//})
//
//var App = React.createClass({
//    render: function() {
//        return (
//            <div className="main">
//                <Header/>
//                <ChatContainer />
//            </div>
//        );
//    }
//})
//
//// ReactDOM.render(
////     <App />,
////     document.getElementById('app')
//// );
//
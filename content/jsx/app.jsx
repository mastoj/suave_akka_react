// Action types
const LOGIN = "LOGIN";
const LOGIN_RESULT = "LOGIN_RESULT";
const CREATE_ROOM = "CREATE_ROOM";
const ROOM_CREATED = "ROOM_CREATED";
const JOIN_ROOM = "JOIN_ROOM";
const USER_JOINED = "USER_JOINED";
const SEND_MESSAGE = "SEND_MESSAGE";
const MESSAGE_RECIEVED = "MESSAGE_RECIEVED";

// Action creators
const login = (userName) => {
    return {
        type: JOIN_CHAT,
        userName
    };
};

const createRoom = (userName, roomName) => {
    return {
        type: CREATE_ROOM,
        userName,
        roomName
    };
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

// Reducers
const FAILED = "FAILED";
const OK = "OK";
const NOT_STARTED = "NOT_STARTED";
const header = (state = {
    login: NOT_STARTED,
    userName: "tomas"
},action) => {
    console.log("In header reducer");
    console.log(action);
    console.log(state);
    switch(action.type) {
        case LOGIN: 
            return Object.assign({}, state, {
                login: FAILED
            });
        case LOGIN_RESULT: 
            if(action.status == "OK") {
                return Object.assign({}, state, {
                    userName: action.userName,
                    login: OK
                });
            }
        default:
            return state;
    }
};

const roomList = (state = {
    roomList: []
}, action) => {
    switch(action.type) {
        case ROOM_CREATED:
            return Object.assign({}, state, {
                roomList: [...state.roomList, action.roomName]
            });
        default:
            return state;
    }    
};

const {combineReducers} = Redux;

const chatApp = combineReducers({
    header,
    roomList
});

const {Component} = React;
const {createStore} = Redux;
const {connect,Provider} = ReactRedux;
const {render} = ReactDOM;

// Components
class HeaderView extends Component {
    render() {
        const {userName} = this.props;
        console.log(this.props);
        return (
            <div>
                This is the header {this.props.userName}
            </div>
        )
    }
}


const mapStateToHeaderProps = (state) => {
    console.log("In props");
    console.log(state);
    return {
        userName: state.header.userName,
        login: state.header.login
    }
}

const Header = connect(
  mapStateToHeaderProps
//   ,
//   mapDispatchToProps
)(HeaderView)

class ChatApp extends Component {
    render() {
        const { dispatch, roomList, userInfo } = this.props;
        return (
            <div>
                <Header />
            </div>
        )
    }
}
                    // userInfo={userInfo}
                    // onLoginClick={dispatch(userName => dispatch(login(userName)))}
                    // ></Header>
                // <RoomList 
                //     roomList={roomList} />

const store = createStore(chatApp)

const rootElement = document.getElementById('app')
render(
  <Provider store={store}>
    <ChatApp />
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
// });

var RoomList = React.createClass({
    render: function() {
        return (
            <div className="room-list">
                List of rooms
            </div>  
        );
    }
});

var Chat = React.createClass({
    render: function() {
        return(
            <div className="chat-window">
            This is a chat window
            </div>
        );
    }    
})

var UserList = React.createClass({
    render: function() {
        return(
            <div className="user-list">
            This is the user list
            </div>
        );
    }    
})

var ChatRoom = React.createClass({
    render: function() {
        return(
            <div className="chat-room">
                <Chat />
                <UserList />
            </div>
        );
    }
})

var ChatContainer = React.createClass({
    render: function() {
        return (
            <div className="chat-container">
                <aside>
                    <RoomList />
                </aside>
                <Chat />
            </div>
        );
    }
})

var App = React.createClass({
    render: function() {
        return (
            <div className="main">
                <Header/>
                <ChatContainer />
            </div>
        );
    }
})

// ReactDOM.render(
//     <App />,
//     document.getElementById('app')
// );

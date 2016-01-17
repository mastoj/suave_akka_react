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
const login = (login) => {
    return {
        type: JOIN_CHAT,
        userName
    };
};

const createRoom = (userName, roomName) = {
    return {
        type: CREATE_ROOM,
        userName,
        roomName
    };
};

const joinRoom = (userName, roomName) = {
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
const chat = (state = {
    userName: "",
    roomList: []
},action) => {
    switch(action.type) {
        case ROOM_CREATED:
            return Object.assign({}, state, {
                roomList: [...state.roomList, action.roomName]
            });
        case LOGIN_RESULT: 
            if(action.status == "OK") {
                return Object.assign({}, state, {
                    userName: action.userName
                });
            }
    }
};

const room = (state  = [], action) => {
    switch(action.type) {
        case ROOM_CREATED:
                        
    }
};



var Header = React.createClass({
  render: function() {
    return (
      <div className="header">
        This is the header
      </div>
    );
  }
});

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

ReactDOM.render(
    <App />,
    document.getElementById('app')
);
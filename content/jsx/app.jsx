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
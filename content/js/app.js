(function(){
    var createRoom = function(){
        var request = new XMLHttpRequest();
        request.open('POST', '/api/room/test', true);
        request.setRequestHeader('Content-Type', 'application/json; charset=UTF-8');
        request.send();
    };
    var joinRoom = function(){
        var root = "ws://" + window.location.hostname;
        if (window.location.port != "") root = root + ":" + window.location.port;
        root = root + "/";
        var websocket = new WebSocket(root + "api/room/test/join/tomas");
        websocket.onmessage = function(evt) {
            console.log(evt.data);
        }
        websocket.onopen = function() {
            websocket.send("This is from the web");            
        };
    };
    createRoom();
    joinRoom();
})();
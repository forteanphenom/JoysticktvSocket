using System;
using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JoysticktvSocket;

public class JoystickMessage
{
    /// <summary>The JSON exactly as the socket recives it from Joystick.tv.</summary>
    public string rawData { get; init; }
    public MessageType type { get; init; }
    public string? user { get; init; }
    public int? tipAmount { get; init; } //used for tips and also wheel spins
    public string? prize { get; init; } //the tip menu item OR the reward from the wheel spin OR a met tip goal
    public string? channelID { get; init; } //id and channelID for the message
    public string? messageID { get; init; }
    public DateTime time { get; init; }
    public int? count { get; init; } //viewers, subs, or followers, on an update event
    public DateTime timerEnds { get; init; }
    public string? timerName { get; init; }
    public string? text { get; init; }    // message as it appears in chat
    public (string emote, string emoteUrl)[]? emotes { get; init; }
    public bool? isFromStreamer { get; init; }
    public bool? isFromModerator { get; init; }
    public bool? isFromSubscriber { get; init; }
    public string? streamEventType { get; init; } //the type of stream event, directly as received from the websocket
                                                  //this is so that i can see and collect new stream events as they happen and add them as types
    public string? streamEventMetadata { get; init; } //this is so that i can collect the metadata structures for those events
}

//the class that contains our websocket, whose methods we will use to send and receive messages
public class JoystickConnection
{
    private ClientWebSocket ws;
    private Thread listenerThread;

    private CancellationTokenSource canceller;

    /// <summary>
    /// returns true of the attached websocket is open, and false otherwise
    /// </summary>
    public bool isSocketOpen
    {
        get
        {
            if (ws == null) return false;
            return (ws.State == WebSocketState.Open);
        }
    }
    public bool isListening
    {
        get
        {
            if (listenerThread == null) return false;
            return listenerThread.IsAlive;
        }
    }

    /// <summary>
    /// This event is triggered every time a message is received, when using ConnectAndListen()
    /// </summary>
    public event EventHandler<JoystickMessage> OnMessageReceived;

    /// <summary>
    /// Connects synchronoursly to Joystick.tv's Websocket 
    /// </summary>
    /// <param name="clientID"></param>
    /// <param name="clientSecret"></param>
    /// <returns></returns>
    public JoystickWebsocketStatus Connect(string clientID, string clientSecret)
    {
        //set up my cancelation token
        canceller = new CancellationTokenSource();

        Uri joystuckUri = new($"wss://joystick.tv/cable?token={GetBasicKey(clientID, clientSecret)}"); //make me a URI with my id and secret

        //if i've disposed or not yet created the client, make a new one
        ws = new ClientWebSocket();

        ws.Options.AddSubProtocol("actioncable-v1-json"); //set the subprotocol, as required by joystick.tv

        try
        {
            ws.ConnectAsync(joystuckUri, canceller.Token).Wait(); //connect
        }
        catch
        {
            return JoystickWebsocketStatus.FailedInitialConnection;
        }

        Receive(); //receive the welcome message
                   //don't need to do anything with this

        //subscribe to websocket connection
        Send("{\"command\":\"subscribe\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\"}");

        //if we don't get a subscribe success, let the user know
        string confirmMessage = "{\"type\":\"confirm_subscription\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\"}";
        if (!isSocketOpen || Receive().rawData != confirmMessage) return JoystickWebsocketStatus.FailedSocketSubscription;

        return JoystickWebsocketStatus.Success;
    }
    /// <summary>
    /// Listens fof a message from the Joystick Websocket, once open.
    /// </summary>
    /// <returns></returns>
    public JoystickMessage Receive()
    {
        //if we try to receive when the websocket is not open, return an unknown message
        if (!isSocketOpen)
        {
            throw new InvalidOperationException("Cannot Receive on a Joystick Websocket that is not Open");
        }

        string msg = "";
        bool completed = false;

        while (!completed)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[2048]); //make a buffer array segment

            //make a var to store the result, this is NOT the informatin received, which is stored in the buffer
            WebSocketReceiveResult result;

            try
            {
                result = ws.ReceiveAsync(buffer, canceller.Token).Result; //listen for a message and fill that buffer
            }
            catch
            {
                return new JoystickMessage() //if it didn't work, return just an unknown message and the time in UTC
                {
                    type = MessageType.Unknown,
                    time = DateTime.Now.ToUniversalTime()
                };
            }

            //add what i've received to the message
            msg += Encoding.UTF8.GetString(buffer);


            completed = result.EndOfMessage;
        }

        return (MessageParser.ParseFromString(msg.Trim('\0'))); //return that buffer's contents as a parsed message
    }
    /// <summary>
    /// Silences the sender od the specivied message, preventing them from further speaking in the specified channel.
    /// </summary>
    /// <param name="messageID"></param>
    /// <param name="channelID"></param>
    /// <returns></returns>
    public JoystickWebsocketStatus SilenceUser(string messageID, string channelID)
    {
        //make sure the websocket is open
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;
        //make sure these ID's work
        if (!MessageParser.CheckValidMessageID(messageID)) return JoystickWebsocketStatus.NotValidMessageID;
        if (!MessageParser.CheckValidChannelID(channelID)) return JoystickWebsocketStatus.NotValidChannelID;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\":\"{\\\"action\\\":\\\"mute_user\\\",\\\"messageId\\\":\\\"" + messageID + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";

        Send(sendMessage);

        return JoystickWebsocketStatus.Success;

    }
    public JoystickWebsocketStatus BlockUser(string messageID, string channelID)
    {
        //make sure the websocket is open
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;
        //make sure these ID's work
        if (!MessageParser.CheckValidMessageID(messageID)) return JoystickWebsocketStatus.NotValidMessageID;
        if (!MessageParser.CheckValidChannelID(channelID)) return JoystickWebsocketStatus.NotValidChannelID;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\":\"{\\\"action\\\":\\\"block_user\\\",\\\"messageId\\\":\\\"" + messageID + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";

        Send(sendMessage);

        return JoystickWebsocketStatus.Success;

    }
    public JoystickWebsocketStatus UnsilenceUser(string user, string channelID)
    {
        //make sure the websocket is open
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;
        //make sure these ID's work
        if (!MessageParser.CheckValidChannelID(channelID)) return JoystickWebsocketStatus.NotValidChannelID;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\":\"{\\\"action\\\":\\\"unmute_user\\\",\\\"username\\\":\\\"" + user + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";


        Send(sendMessage);

        return JoystickWebsocketStatus.Success;

    }

    /// <summary>
    /// Sends the specivied user a whisper in chat.
    /// </summary>
    /// <param name="user"></param>
    /// <param name="channelID"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public JoystickWebsocketStatus Whisper(string user, string channelID, string message)
    {
        //make sure the websocket is open
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;
        //make sure these ID's work
        if (!MessageParser.CheckValidChannelID(channelID)) return JoystickWebsocketStatus.NotValidChannelID;

        message = MessageParser.EscapeCharacters(message);

        //create and send the whisper
        string sendMessage = "{\"command\":\"message\",\"identifier\": \"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\": \"{\\\"action\\\":\\\"send_whisper\\\",\\\"username\\\":\\\"" + user + "\\\",\\\"text\\\": \\\"" + message + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;
    }

    public JoystickWebsocketStatus ChatMessage(string channelID, string message)
    {
        //make sure the websocket is open
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;
        //make sure these ID's work
        if (!MessageParser.CheckValidChannelID(channelID)) return JoystickWebsocketStatus.NotValidChannelID;

        //create and send the message
        string sendMessage = "{\"command\":\"message\",\"identifier\": \"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\": \"{\\\"action\\\":\\\"send_message\\\",\\\"text\\\": \\\"" + MessageParser.EscapeCharacters(message) + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;
    }
    /// <summary>
    /// Removes the message with the specified ID from the specified Joystick channel.
    /// </summary>
    /// <param name="messageID"></param>
    /// <param name="channelID"></param>
    /// <returns></returns>
    public JoystickWebsocketStatus DeleteMessage(string messageID, string channelID)
    {
        //make sure the websocket is open
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;
        //make sure these ID's work
        if (!MessageParser.CheckValidMessageID(messageID)) return JoystickWebsocketStatus.NotValidMessageID;
        if (!MessageParser.CheckValidChannelID(channelID)) return JoystickWebsocketStatus.NotValidChannelID;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\": \"{\\\"action\\\": \\\"delete_message\\\",\\\"messageId\\\": \\\"" + messageID + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";

        Send(sendMessage);

        return JoystickWebsocketStatus.Success;
    }

    private void Send(string msg)
    {
        ArraySegment<byte> buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)); //convert this string into an an arraysegment
        ws.SendAsync(buffer, WebSocketMessageType.Text, true, canceller.Token).Wait(); //send that arraysegment
    }

    public void Close()
    {
        //send out the cancellation token to everything
        canceller.Cancel();

        if (isSocketOpen)
        {
            ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
        }

        //wait for the listener to close
        if (isListening)
        {
            Thread.Sleep(200);
        }

        ws.Dispose();
        ws = null;


    }
    /// <summary>
    /// Connects to the Joystick Websocket, and sends an OnMessageReceived event every time a message is received.
    /// </summary>
    /// <param name="clientID"></param>
    /// <param name="clientSecret"></param>
    /// <returns></returns>
    public void ConnectAndListen(string clientID, string clientSecret)
    {
        listenerThread = new Thread(() => Listen(clientID, clientSecret));
        listenerThread.Start();
    }

    private void Listen(string clientID, string clientSecret)
    {
        Connect(clientID, clientSecret);
        //until someone calls Close()
        while (!canceller.IsCancellationRequested)
        {
            while (isSocketOpen && !canceller.IsCancellationRequested)
            {
                JoystickMessage _msg = Receive();
                OnMessageReceived?.Invoke(this, _msg);
            }
            //if we aren't closing this, reconnect
            if (!canceller.IsCancellationRequested)
            {
                JoystickWebsocketStatus status = Connect(clientID, clientSecret);
                //if the connection was not successful, wait 3 seconds
                if (status != JoystickWebsocketStatus.Success) Thread.Sleep(3000);
            }
        }
    }

    private string GetBasicKey(string clientId, string clientSecret)
    {
        //convert it to bytes
        byte[] plainTextBytes = System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        //convert those bytes to base64
        string BasicKey = System.Convert.ToBase64String(plainTextBytes);

        return BasicKey;
    }

    //static class that is just used to convert the incoming serialized JSON into JoystickMessage
    private static class MessageParser
    {
        //this is to check that channelIDs and messageIDs are valid
        private static char[] hexchars = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };

        public static JoystickMessage ParseFromString(string message)
        {
            string? _rawData = message; //i store this here rather that just putting message into the new JoystickMessage because
                                        //i strip the identifier section from the message before the end of the method

            //get the details
            MessageType _type = GetType(message);
            string? _user = null;
            DateTime _time = DateTime.MinValue;
            string? _messageID = null;
            string? _channelID = null;
            string? _streamEventType = null;
            string? _streamEventMetadata = null;
            int? _tipAmount = null;
            string? _prize = null;
            bool? _isFromSubscriber = null;
            bool? _isFromModerator = null;
            bool? _isFromStreamer = null;
            string? _text = null;
            int? _count = null; //count of viewers, followers, or subscribers
            string? _timerName = null;
            DateTime _timerEnds = DateTime.MinValue;
            List<(string emote, string emoteUrl)> _emotes = new(); //do this as a list to add the things, we convert it to an array in the end


            //for unknown message types, all we get is a time, and that comes trom the system clock
            if (_type == MessageType.Unknown)
            {
                _time = DateTime.Now.ToUniversalTime();
            }

            //for pings we also get a time, stored as a unit timestamp
            else if (_type == MessageType.Ping)
            {
                RawMessages.Ping messageData = JsonSerializer.Deserialize<RawMessages.Ping>(message);
                _time = DateTimeOffset.FromUnixTimeSeconds((long)messageData.message).DateTime;
            }
            //is this a stream presence event
            else if (_type == MessageType.UserEnter || _type == MessageType.UserLeave)
            {
                message = StripIdentifier(message);

                RawMessages.UserEvent data = JsonSerializer.Deserialize<RawMessages.UserEvent>(message);

                _messageID = data.id;
                _channelID = data.channelId;

                _time = (DateTime)data.createdAt;

                //the text field seems to contain who entered or left
                _user = data.text;
            }
            //is this a streamEvent?
            else if ((int)_type >= 300)
            {
                message = StripIdentifier(message);

                //get the data
                RawMessages.StreamEvent data = JsonSerializer.Deserialize<RawMessages.StreamEvent>(message);

                _channelID = data.channelId;
                _messageID = data.id;

                _time = (DateTime)data.createdAt;
                _text = data.text;

                //this is stored because i can't predict every stream event, as they will likely toncintue to change over time
                _streamEventMetadata = data.metadata;
                _streamEventType = data.type;

                //get the metadata
                RawMessages.StreamEvent.MetaData metaData = JsonSerializer.Deserialize<RawMessages.StreamEvent.MetaData>(data.metadata);

                //if it has a WHO that's the user
                if (metaData.who != null) _user = metaData.who;

                //if it has a how_much, that's the tip amount
                if (metaData.how_much != null) _tipAmount = metaData.how_much;

                // get the count
                if (metaData.number_of_viewers != null) _count = metaData.number_of_viewers;
                if (metaData.number_of_followers != null) _count = metaData.number_of_followers;
                if (metaData.number_of_subscribers != null) _count = metaData.number_of_subscribers;
                if (metaData.destination != null) _user = metaData.destination;

                // get the type-specific properties
                if (_type == MessageType.TipLocked || _type == MessageType.TipUnlocked ||
                    _type == MessageType.TipGoal || _type == MessageType.TipGoalUpdate) _prize = metaData.title;
                else if (_type == MessageType.Tip) _prize = metaData.tip_menu_item;
                else if (_type == MessageType.WheelSpin) _prize = metaData.prize;
                else if (_type == MessageType.TimerStarted)
                {
                    _timerEnds = (DateTime)metaData.endsAt;
                    _timerName = metaData.name;
                }
            }
            //is this a chat message?
            else if (_type == MessageType.ChatMessage)
            {

                message = StripIdentifier(message);

                RawMessages.ChatMessage chatMessage = JsonSerializer.Deserialize<RawMessages.ChatMessage>(message);

                _user = chatMessage.author.username;
                _time = (DateTime)chatMessage.createdAt;

                _isFromSubscriber = chatMessage.author.isSubscriber;
                _isFromModerator = chatMessage.author.isModerator;
                _isFromStreamer = chatMessage.author.isStreamer;

                _text = chatMessage.text;

                _channelID = chatMessage.channelId;
                _messageID = chatMessage.messageId;

                //emotes, one at a time
                foreach (RawMessages.ChatMessage.EmoteEntry emoteEntry in chatMessage.emotesUsed)
                {
                    string emote = emoteEntry.code;
                    string emoteUrl = emoteEntry.signedUrl; //idk why this is the right one but it is!
                    _emotes.Add((emote, emoteUrl));
                }
            }

            //initialize a JoystickMessage
            return new JoystickMessage()
            {
                rawData = _rawData,
                type = _type,
                user = _user,
                time = _time,
                messageID = _messageID,
                channelID = _channelID,
                streamEventType = _streamEventType,
                streamEventMetadata = _streamEventMetadata,
                tipAmount = _tipAmount,
                prize = _prize,
                isFromSubscriber = _isFromSubscriber,
                isFromModerator = _isFromModerator,
                isFromStreamer = _isFromStreamer,
                text = _text,
                emotes = _emotes.ToArray(),
                count = _count,
                timerEnds = _timerEnds,
                timerName = _timerName,
            };
        }

        private static string StripIdentifier(string message) //strips the identifier based on character counts.
                                                              //if how the identifier is sent changes, i need to redo this
        {
            message = message.Substring(59, message.Length - 60);
            return message;
        }

        private static MessageType GetType(string message)
        {
            if (message.StartsWith("{\"type\":\"ping\"")) return MessageType.Ping;
            if (message.Contains("\"event\":\"ChatMessage\"")) return MessageType.ChatMessage;

            if (message.Contains("\"event\":\"UserPresence\""))
            {
                if (message.Contains("\"type\":\"enter_stream\"")) return MessageType.UserEnter;
                else if (message.Contains("\"type\":\"leave_stream\"")) return MessageType.UserLeave;
            }

            if (message.Contains("\"event\":\"StreamEvent\""))
            {
                if (message.Contains("\"type\":\"Followed\"")) return MessageType.NewFollower;
                else if (message.Contains("\"type\":\"SettingsUpdated\"")) return MessageType.SettingsUpdated;
                else if (message.Contains("\"type\":\"Tipped\"")) return MessageType.Tip;
                else if (message.Contains("\"type\":\"WheelSpinClaimed\"")) return MessageType.WheelSpin;
                else if (message.Contains("\"type\":\"StreamDroppedIn\"")) return MessageType.DropIn; //this the STREAMER dropping in on someone, NOT someone dropping in on the streamer
                else if (message.Contains("\"type\":\"Subscribed\"")) return MessageType.NewSubscriber;
                else if (message.Contains("\"type\":\"GiftedSubscriptions\"")) return MessageType.GiftSubs;
                else if (message.Contains("\"type\":\"TipMenuItemLocked\"")) return MessageType.TipLocked;
                else if (message.Contains("\"type\":\"TipMenuItemUnlocked\"")) return MessageType.TipUnlocked;
                else if (message.Contains("\"type\":\"TipGoalMet\"")) return MessageType.TipGoal;
                else if (message.Contains("\"type\":\"TipGoalUpdated\"")) return MessageType.TipGoalUpdate;
                else if (message.Contains("\"type\":\"SubscriberOnlyStarted\"")) return MessageType.SubOnlyStarted;
                else if (message.Contains("\"type\":\"SubscriberOnlyEnded\"")) return MessageType.SubOnlyEnded;
                else if (message.Contains("\"type\":\"ViewerCountUpdated\"")) return MessageType.ViewerCountUpdate;
                else if (message.Contains("\"type\":\"SubscriberCountUpdated\"")) return MessageType.SubCountUpdate;
                else if (message.Contains("\"type\":\"FollowerCountUpdated\"")) return MessageType.FollowerCountUpdate;
                else if (message.Contains("\"type\":\"DeviceConnected\"")) return MessageType.DeviceConnected;
                else if (message.Contains("\"type\":\"DeviceSettingsUpdated\"")) return MessageType.DeviceSettingsUpdated;
                else if (message.Contains("\"type\":\"Started\"")) return MessageType.StreamStart;
                else if (message.Contains("\"type\":\"Ended\"")) return MessageType.StreamEnd;
                else if (message.Contains("\"type\":\"ChatTimerStarted\"")) return MessageType.TimerStarted;
                else if (message.Contains("\"type\":\"DropinStream\"")) return MessageType.DropIn;
                else if (message.Contains("\"type\":\"VerifiedOnlyChatStarted\"")) return MessageType.VerifiedOnlyStarted;
                else if (message.Contains("\"type\":\"VerifiedOnlyChatEnded\"")) return MessageType.VerifiedOnlyEnded;
                else return MessageType.UnknownStreamEvent;
            }

            //subscribe success and rejections, as well as welcomes, return this,
            //because there is no reason for the user of this library should see those events
            return MessageType.Unknown;
        }

        public static bool CheckValidChannelID(string channelID)
        {
            if (channelID == null || channelID.Length != 64) return false;
            foreach (char ch in channelID)
            {
                if (!hexchars.Contains(ch)) return false;
            }
            return true;
        }
        public static bool CheckValidMessageID(string messageID)
        {
            //make sure it's the right length
            if (messageID == null || messageID.Length != 36) return false;

            //make an array of characters
            for (int i = 0; i < messageID.Length; i++)
            {
                //if it should be a - make sure it's a -
                if (i == 8 || i == 13 || i == 18 || i == 23)
                {
                    if (messageID[i] != '-') return false;
                }
                //otherwise make sure it's a hex character
                else
                {
                    if (!hexchars.Contains(messageID[i])) return false;
                }
            }
            return true;
        }

        public static string EscapeCharacters(string message)
        {
            int length = message.Length;
            for (int i = length - 1; i >= 0; i--)
            {
                //we need MANY escape characters because of the way this will be deserialized
                if (message[i] == '"') message = message.Insert(i, "\\\\\\");
                else if (message[i] == '\\') message = message.Insert(i, "\\\\\\");
                else if (message[i] == '\n') message = message.Insert(i, "\\\\\\");
            }

            return message;
        }

        //private class used to deserialize JSON
        private class RawMessages
        {
            //a lot of these are never ereferenced, but included in case i decide to reference them later.
            public class UserEvent
            {
                public string? id { get; set; } //message ID
                public string? @event { get; set; }
                public string? type { get; set; } //either enter_stream or leave_stream
                public string? text { get; set; } //the username of whomst entered or left
                public string? channelId { get; set; } //unique channel id
                public DateTime? createdAt { get; set; } //as a string in ztime
            }
            public class Ping
            {
                public string? type { get; set; }
                public long? message { get; set; }
            }
            public class ChatMessage
            {
                public DateTime? createdAt { get; set; }
                public string? messageId { get; set; }
                public string? type { get; set; }
                public string? visibility { get; set; }
                public string? text { get; set; }
                public string? botCommand { get; set; }
                public string? botCommandArg { get; set; }

                public EmoteEntry[]? emotesUsed { get; set; }
                public class EmoteEntry
                {
                    public string? id { get; set; }
                    public string? code { get; set; }
                    public string? url { get; set; }
                    public string? signedUrl { get; set; }
                    public string? thumbnailUrl { get; set; }
                    public string? signedThumbailUrl { get; set; }
                }
                public Author? author { get; set; }
                public class Author
                {
                    public string? slug { get; set; }
                    public string? username { get; set; }
                    //may put this back in, it'll require a little trial and error to see how to parse it
                    //since it's not covered in the documentionation, but i don't plan to use it anyway
                    //"usernameColor": null
                    public string? displayNameWithFlair { get; set; }
                    public string? signedPhotoUrl { get; set; }
                    public string? signedPhotoThumbUrl { get; set; }
                    public bool? isStreamer { get; set; }
                    public bool? isModerator { get; set; }
                    public bool? isSubscriber { get; set; }
                }
                public Streamer? streamer { get; set; }
                public class Streamer
                {
                    public string? slug { get; set; }
                    public string? username { get; set; }
                    //"usernameColor": null
                    public string? signedPhotoUrl { get; set; }
                    public string? signedPhotoThumbUrl { get; set; }
                }
                public string? channelId { get; set; }
                public bool? mention { get; set; }
                //"mentionedUsername": null
            }
            public class StreamEvent
            {
                public string? id { get; set; }
                public string? @event { get; set; }
                public string? type { get; set; }
                public string? text { get; set; }
                public string? metadata { get; set; }
                public DateTime? createdAt { get; set; }
                public string? channelId { get; set; }

                public class MetaData
                {
                    public string? who { get; set; }
                    public string? what { get; set; }
                    public int? how_much { get; set; } //for tips AND wheel spins
                    public string? prize { get; set; } // for wheel spins
                    public string? tip_menu_item { get; set; } // for tips
                    public int? number_of_viewers { get; set; } // for viewer count updates
                    public string? name { get; set; } //for timer set
                    public DateTime? endsAt { get; set; } //for timer set
                    public int? number_of_subscribers { get; set; } //sub count update
                    public int? number_of_followers { get; set; } // for follower count updates
                    public string? title { get; set; } // the prize for tip lock and tip unlock
                    public string? destination { get; set; }//the destination of a drop in
                }
            }
        }
    }
}

//public enum of message types
public enum MessageType
{
    Unknown = -1,
    //websocket messages
    Ping = 0,
    //chat events
    ChatMessage = 100,
    //user events
    UserEnter = 200,
    UserLeave = 201,
    //stream events
    StreamStart = 300,
    StreamEnd = 301,
    Tip = 302,
    WheelSpin = 303,
    NewFollower = 304,
    FollowerCountUpdate = 305,
    NewSubscriber = 306,
    GiftSubs = 307,
    SubCountUpdate = 308,
    DropIn = 309,
    TipGoal = 310,
    SubOnlyStarted = 311,
    SubOnlyEnded = 312,
    ViewerCountUpdate = 313,
    TimerStarted = 314,
    //TimerEnded is not a chat event yet but leave room here for that if they add it
    TipLocked = 316,
    TipUnlocked = 317,
    SettingsUpdated = 318,
    DeviceConnected = 319,
    DeviceSettingsUpdated = 320,
    TipGoalUpdate = 321,
    VerifiedOnlyStarted = 322,
    VerifiedOnlyEnded = 323,
    UnknownStreamEvent = 399,
}

//public enum of potential errors for send, connect, and close methods
public enum JoystickWebsocketStatus
{
    Success,
    FailedInitialConnection,
    FailedSocketSubscription,
    SocketNotOpen,
    NotValidChannelID,
    NotValidMessageID
}
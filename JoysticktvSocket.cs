using System;
using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace JoysticktvSocket;

/// <summary>An object representing the message or event received from Joystick.tv</summary>
public class JoystickMessage : EventArgs
{
    /// <summary>The JSON exactly as the socket recives it from Joystick.tv.</summary>
    public string rawData { get; init; }
    public MessageType type { get; init; }
    public string? streamerName { get; init; }
    public string? user { get; init; }
    public int? tipAmount { get; init; } //used for tips and also wheel spins
    public string? prize { get; init; } //the tip menu item OR the reward from the wheel spin OR a met tip goal
    public string? channelID { get; init; } //id and channelID for the message
    public string? messageID { get; init; }
    public DateTime time { get; init; }
    public int? count { get; init; } //viewers, subs, or followers, on an update event
    public DateTime? timerEnds { get; init; }
    public string? timerName { get; init; }
    public string text { get; init; }    // message as it appears in chat
    public (string emote, string emoteUrl)[]? emotes { get; init; }
    public bool? isFromStreamer { get; init; }
    public bool? isFromModerator { get; init; }
    public bool? isFromSubscriber { get; init; }
    public string? streamEventType { get; init; } //the type of stream event, directly as received from the websocket
                                                  //this is so that i can see and collect new stream events as they happen and add them as types
    public string? streamEventMetadata { get; init; } //this is so that i can collect the metadata structures for those events

    public JoystickMessage()
    {
        type = MessageType.Unknown;
        time = DateTime.Now;
        text = "";
    }
    public JoystickMessage(string message)
    {
        rawData = message;

        if (message.StartsWith("{\"type\":\"ping\""))
        {
            type = MessageType.Ping;
            RawMessages.Ping messageData = JsonSerializer.Deserialize<RawMessages.Ping>(message);
            time = DateTimeOffset.FromUnixTimeSeconds((long)messageData.message).DateTime;
            return;
        }
        else if (message.StartsWith("{\"type\""))
        {
            type = MessageType.Unknown;
            time = DateTime.Now;
            return;
        }

        RawMessages.ParsedEvent data;
        try
        {
            data = JsonSerializer.Deserialize<RawMessages.ParsedEvent>(StripIdentifier(message));
        }
        catch
        {
            // if we are unable to parse it, then deliver it as unknown
            type = MessageType.Unknown;
            time = DateTime.Now;
            return;
        }
        
        type = GetMessageType(data.@event, data.type);
            
        time = data.createdAt ?? DateTime.Now;
        channelID = data.channelId;
        messageID = data.messageId ?? data.id;

        if (type == MessageType.UserEnter || type == MessageType.UserLeave) user = data.text;

        else if ((int)type >= 300 && type != MessageType.UnknownStreamEvent)
        {
            text = data.text ?? "";
            streamEventMetadata = data.metadata;
            streamEventType = data.type;

            RawMessages.ParsedEvent.MetaData metaData = JsonSerializer.Deserialize<RawMessages.ParsedEvent.MetaData>(data.metadata ?? "{}");

            streamerName = metaData.where; // this is only given on PVP events

            user = metaData.destination_username ?? metaData.who;
            if (type == MessageType.Tip || type == MessageType.WheelSpin)
            {
                tipAmount = metaData.how_much;
            }
            count = metaData.number_of_viewers ?? metaData.number_of_followers ?? metaData.number_of_subscribers;
            prize = metaData.title ?? metaData.tip_menu_item ?? metaData.prize;
            timerEnds = metaData.endsAt;
            timerName = (type == MessageType.TimerStarted) ? metaData.name : null; //SO LONG AS ONLY TIMERS HAVE A NAME
        }

        else if (type == MessageType.ChatMessage)
        {
            streamerName = data.streamer.username;
            user = data.author.username;
            isFromSubscriber = data.author.isSubscriber;
            isFromModerator = data.author.isModerator;
            isFromStreamer = data.author.isStreamer;
            text = data.text;

            //emotes, one at a time
            List<(string emote, string emoteUrl)> _emotes = new(); //do this as a list to add the things, we convert it to an array in the end
            foreach (RawMessages.ParsedEvent.EmoteEntry emoteEntry in data.emotesUsed)
                _emotes.Add((emoteEntry.code, emoteEntry.signedUrl));
            emotes = _emotes.ToArray();
        }
        else if (type == MessageType.BotMessage)
        {
            user = data.author.username;
            text = data.text;
        }
    }

    private static MessageType GetMessageType(string? @event, string? type)
    {
        if (@event == "ChatMessage") return MessageType.ChatMessage;
        if (@event == "BotMessage") return MessageType.BotMessage;
        if (type == "enter_stream") return MessageType.UserEnter;
        if (type == "leave_stream") return MessageType.UserLeave;
        if (type == "Followed") return MessageType.NewFollower;
        if (type == "SettingsUpdated") return MessageType.SettingsUpdated;
        if (type == "Tipped") return MessageType.Tip;
        if (type == "WheelSpinClaimed") return MessageType.WheelSpin;
        if (type == "StreamDroppedIn") return MessageType.DropIn;
        if (type == "DropinStream") return MessageType.OutgoingDropIn;
        if (type == "Subscribed") return MessageType.NewSubscriber;
        if (type == "GiftedSubscriptions") return MessageType.GiftSubs;
        if (type == "TipMenuItemLocked") return MessageType.TipLocked;
        if (type == "TipMenuItemUnlocked") return MessageType.TipUnlocked;
        if (type == "TipGoalMet") return MessageType.TipGoal;
        if (type == "TipGoalUpdated") return MessageType.TipGoalUpdate;
        if (type == "TipGoalIncreased") return MessageType.TipGoalIncreased;
        if (type == "StreamModeUpdated") return MessageType.StreamModeUpdated;
        if (type == "ViewerCountUpdated") return MessageType.ViewerCountUpdate;
        if (type == "SubscriberCountUpdated") return MessageType.SubCountUpdate;
        if (type == "FollowerCountUpdated") return MessageType.FollowerCountUpdate;
        if (type == "DeviceConnected") return MessageType.DeviceConnected;
        if (type == "DeviceSettingsUpdated") return MessageType.DeviceSettingsUpdated;
        if (type == "Started") return MessageType.StreamStart;
        if (type == "Ended") return MessageType.StreamEnd;
        if (type == "ChatTimerStarted") return MessageType.TimerStarted;
        if (type == "VerifiedOnlyChatStarted") return MessageType.VerifiedOnlyStarted;
        if (type == "VerifiedOnlyChatEnded") return MessageType.VerifiedOnlyEnded;
        if (type == "MilestoneCompleted") return MessageType.MilestoneCompleted;
        if (type == "DeviceDisconnected") return MessageType.DeviceDisconnected;
        if (type == "UserMuted") return MessageType.Mute;
        if (type == "UserUnmuted") return MessageType.Unmute;
        if (type == "PvpSessionRequested") return MessageType.PvpRequested;
        if (type == "PvpSessionReady") return MessageType.PvpReady;
        if (type == "PvpSessionStarted") return MessageType.PvpStarted;
        if (type == "PvpSessionEnded") return MessageType.PvpEnded;

        if (@event == "StreamEvent") return MessageType.UnknownStreamEvent;
        return MessageType.Unknown;
    }

    public override string ToString()
    {
        string result = "[" + time + "] " + type.ToString();
        if (user != null) result += " from " + user.ToString();
        if (text != null) result += ": " + text;
        if (streamerName != null) result += "\nStreamer: " + streamerName;
        if (tipAmount != null) result += "\nTip Amount: " + tipAmount;
        if (prize != null) result += "\nPrize: " + prize;
        if (channelID != null) result += "\nChannel ID: " + channelID;
        if (messageID != null) result += "\nMessage ID: " + messageID;
        if (streamEventType != null) result += "\nEvent Type: " + streamEventType;
        if (streamEventMetadata != null) result += "\nEvent Meta: " + streamEventMetadata;
        result += "\n";
        return result;
    }

    internal static string StripIdentifier(string message)
    {
        // this prevents messages that are too short from being trimmed, and then they can be returned unexamined
        if (message.Length < 60) return message;
        return message.Substring(59, message.Length - 60); ;
    } //gets rid of some junk in the json
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

    public bool TroubleshootOutput = false;

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

        Receive(); //receive the welcome message.  don't need to do anything with this

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
        if (!isSocketOpen) throw new InvalidOperationException("Cannot Receive on a Joystick Websocket that is not Open");

        string msg = "";
        bool completed = false;

        while (!completed)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[2048]); //make a buffer array segment

            //make a var to store the result, this is NOT the informatin received, which is stored in the buffer
            WebSocketReceiveResult result;

            try { result = ws.ReceiveAsync(buffer, canceller.Token).Result; } //listen for a message and fill that buffer
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

        return (new JoystickMessage(msg.Trim('\0'))); //return that buffer's contents as a parsed message
    }
    /// <summary>
    /// Silences the sender od the specivied message, preventing them from further speaking in the specified channel.
    /// </summary>
    /// <param name="messageID"></param>
    /// <param name="channelID"></param>
    /// <returns></returns>
    public JoystickWebsocketStatus SilenceUser(string messageID, string channelID)
    {
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\":\"{\\\"action\\\":\\\"mute_user\\\",\\\"messageId\\\":\\\"" + messageID + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;

    }
    public JoystickWebsocketStatus BlockUser(string messageID, string channelID)
    {
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\":\"{\\\"action\\\":\\\"block_user\\\",\\\"messageId\\\":\\\"" + messageID + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;
    }
    public JoystickWebsocketStatus UnsilenceUser(string user, string channelID)
    {
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;

        string sendMessage = "{\"command\":\"message\",\"identifier\":\"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\":\"{\\\"action\\\":\\\"unmute_user\\\",\\\"username\\\":\\\"" + user + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;

    }

    /// <summary>
    /// Sends the specivied user a whisper in chat.
    /// </summary>
    public JoystickWebsocketStatus Whisper(string user, string channelID, string message)
    {
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;

        string sendMessage = "{\"command\":\"message\",\"identifier\": \"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\": \"{\\\"action\\\":\\\"send_whisper\\\",\\\"username\\\":\\\"" + user + "\\\",\\\"text\\\": \\\"" + EscapeCharacters(message) + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;
    }

    public JoystickWebsocketStatus ChatMessage(string channelID, string message)
    {
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;

        string sendMessage = "{\"command\":\"message\",\"identifier\": \"{\\\"channel\\\":\\\"GatewayChannel\\\"}\",\"data\": \"{\\\"action\\\":\\\"send_message\\\",\\\"text\\\": \\\"" + EscapeCharacters(message) + "\\\",\\\"channelId\\\":\\\"" + channelID + "\\\"}\"}";
        Send(sendMessage);

        return JoystickWebsocketStatus.Success;
    }
    /// <summary>
    /// Removes the message with the specified ID from the specified Joystick channel.
    /// </summary>
    public JoystickWebsocketStatus DeleteMessage(string messageID, string channelID)
    {
        if (!isSocketOpen) return JoystickWebsocketStatus.SocketNotOpen;

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
            ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();

        //wait for the listener to close
        if (isListening) Thread.Sleep(200);

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
        if (TroubleshootOutput) Console.WriteLine("Connecting.");
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
                if (TroubleshootOutput) Console.WriteLine("Disconnected,  Reconnecting.");
                JoystickWebsocketStatus status = Connect(clientID, clientSecret);
                //if the connection was not successful, wait 3 seconds
                if (status != JoystickWebsocketStatus.Success)
                {
                    if (TroubleshootOutput) Console.WriteLine("Reconnect failed, waiting 3 seconds.");
                    Thread.Sleep(3000);
                }
                else if (TroubleshootOutput) Console.WriteLine("Reconnected.");
            }
        }
    }

    private string GetBasicKey(string clientId, string clientSecret)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
        return Convert.ToBase64String(plainTextBytes); ;
    }

    public static string EscapeCharacters(string message)
    {
        for (int i = message.Length - 1; i >= 0; i--)
            if (message[i] == '"' || message[i] == '\\') message = message.Insert(i, "\\\\\\");
        return message;
    }

}


//private class used to deserialize JSON
internal class RawMessages
{
    public class Ping
    {
        public long? message { get; set; }
    }
    public class ParsedEvent
    {
        public string @event { get; set; }
        public string? messageId { get; set; }
        public string? id { get; set; }
        public string? type { get; set; }
        public string? text { get; set; }
        public string? metadata { get; set; }
        public DateTime? createdAt { get; set; }
        public string? channelId { get; set; }

        public EmoteEntry[]? emotesUsed { get; set; }
        public class EmoteEntry
        {
            public string? code { get; set; }
            public string? signedUrl { get; set; }
        }
        public Author? author { get; set; }
        public class Author
        {
            public string? username { get; set; }
            public string? signedPhotoUrl { get; set; }
            public string? signedPhotoThumbUrl { get; set; }
            public bool? isStreamer { get; set; }
            public bool? isModerator { get; set; }
            public bool? isSubscriber { get; set; }
        }
        public Streamer? streamer { get; set; }

        public class Streamer
        {
            public string? username { get; set; }
        }

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
            public string? destination_username { get; set; }//the destination of a drop in
            public string? where { get; set; } // the streamer hosting a private
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
    BotMessage = 101,
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
    Resubscribed = 309,
    DropIn = 310,
    OutgoingDropIn = 311,
    TipGoal = 312,
    StreamModeUpdated = 313,
    ViewerCountUpdate = 314,
    TimerStarted = 315,
    TipLocked = 316,
    TipUnlocked = 317,
    SettingsUpdated = 318,
    DeviceConnected = 319,
    DeviceDisconnected = 320,
    DeviceSettingsUpdated = 321,
    VerifiedOnlyStarted = 322,
    VerifiedOnlyEnded = 323,
    MilestoneCompleted = 324,
    TipGoalUpdate = 325,
    TipGoalIncreased = 326,
    /// <summary>
    /// An overlay scene has its configs updated
    /// </summary>
    SceneUpdated = 327,
    Mute = 330,
    Unmute = 331,
    
    PvpRequested = 350,
    PvpReady = 351,
    PvpStarted = 352,
    PvpEnded = 353,

    UnknownStreamEvent = 399,
}

//public enum of potential errors for send, connect, and close methods
public enum JoystickWebsocketStatus
{
    Success,
    FailedInitialConnection,
    FailedSocketSubscription,
    SocketNotOpen,
}
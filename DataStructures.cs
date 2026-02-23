using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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
        public bool? isFromContentCreator { get; init; }
        public bool? isHighlighted { get; init; }
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

            if (message == "")
            {
                type = MessageType.Unknown;
                time = DateTime.Now;
                return;
            }

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

            RawMessages.ParsedEvent data = JsonSerializer.Deserialize<RawMessages.ParsedEvent>(StripIdentifier(message));
            type = GetMessageType(data.@event, data.type);
            time = data.createdAt ?? DateTime.Now;
            channelID = data.channelId;
            messageID = data.messageId ?? data.id;

            if (type == MessageType.UserEnter || type == MessageType.UserLeave) user = data.text;

            else if ((int)type >= 300)
            {
                text = data.text ?? "";
                streamEventMetadata = data.metadata;
                streamEventType = data.type;

                RawMessages.ParsedEvent.MetaData metaData = JsonSerializer.Deserialize<RawMessages.ParsedEvent.MetaData>(data.metadata ?? "{}");

                user = metaData.destination_username ?? metaData.who;
                tipAmount = metaData.how_much;
                count = metaData.number_of_viewers ?? metaData.number_of_followers ?? metaData.number_of_subscribers;
                prize = metaData.title ?? metaData.tip_menu_item ?? metaData.prize;
                timerEnds = metaData.endsAt;
                timerName = metaData.name; //SO LONG AS ONLY TIMERS HAVE A NAME
            }

            else if (type == MessageType.ChatMessage)
            {
                streamerName = data.streamer.username;
                user = data.author.username;
                isFromSubscriber = data.author.isSubscriber;
                isFromModerator = data.author.isModerator;
                isFromStreamer = data.author.isStreamer;
                isFromContentCreator = data.author.isContentCreator;
                isHighlighted = data.highlight;
                text = data.text;

                //emotes, one at a time
                List<(string emote, string emoteUrl)> _emotes = new(); //do this as a list to add the things, we convert it to an array in the end
                foreach (RawMessages.ParsedEvent.EmoteEntry emoteEntry in data.emotesUsed)
                    _emotes.Add((emoteEntry.code, emoteEntry.signedUrl));
                emotes = _emotes.ToArray();
            }
            else if (type == MessageType.BotMessage)
            {
                text = data.text ?? "";
            }
        }

        private MessageType GetMessageType(string? @event, string? type)
        {
            if (@event == "ChatMessage") return MessageType.ChatMessage;
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
            if (type == "UserMuted") return MessageType.UserMuted;
            if (type == "UserUnmuted") return MessageType.UserUnmuted;
            if (type == "PvpSessionRequested") return MessageType.PvpRequested;
            if (type == "PvpSessionReady") return MessageType.PvpReady;
            if (type == "PvpSessionStarted") return MessageType.PvpStarted;
            if (type == "PvpSessionEnded") return MessageType.PvpEnded;
            if (type == "Resubscribed") return MessageType.SubRenewed;
            if (@event == "StreamEvent") return MessageType.UnknownStreamEvent;
            if (@event == "BotMessage") return MessageType.BotMessage;
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
            if (message.Length < 60) return message; // this prevents an error from returning if the message length is short.
            return message.Substring(59, message.Length - 60);
        } //gets rid of some junk in the json
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
        public bool? highlight { get; set; }

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
            public bool? isContentCreator { get; set; }
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
        }
    }
}

//public enum of message types
public enum MessageType
{
    Unknown = -1,
    //websocket messages
    Ping = 0,
    BotMessage = 1,
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
    SubRenewed = 307,
    GiftSubs = 308,
    SubCountUpdate = 309,
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
    UserMuted = 326,
    UserUnmuted = 327,
    PvpRequested = 328,
    PvpReady = 329,
    PvpStarted = 330,
    PvpEnded = 331,
    TipGoalIncreased = 332,
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

public class oauthMessage
{
    public string id { get; set; }
    public string access_token { get; set; }
    public oauthMessage()
    {
        id = "";
        access_token = "";
    }
}

public class oauthUserEntry
{
    public string username { get; set; }
    public string channelID { get; set; }
    private DateTime setTime { get; set; }

    public oauthUserEntry(string username, string channelID)
    {
        this.username = username;
        this.channelID = channelID;

        setTime = DateTime.Now;
    }

    public bool CheckExpired()
    {
        return setTime > DateTime.Now.AddHours(-1);
    }
}

public class ErrorMessage
{
    public string type { get; set; }
    public string error { get; set; }
    public ErrorMessage(string error)
    {
        this.type = "error";
        this.error = error;
    }
    public string Serialize()
    {
        return System.Text.Json.JsonSerializer.Serialize(this);
    }
}

public class AccessTokenMessage
{
    public string access_token { get; set; }
    public string refresh_token { get; set; }
    public long expires_in { get; set; }
    public string raw_data { get; set; }

    public AccessTokenMessage()
    {
        access_token = string.Empty;
        refresh_token = string.Empty;
        expires_in = 0;
        raw_data = string.Empty;
    }

    public AccessTokenMessage(string overrideToken)
    {
        access_token = overrideToken;
        refresh_token = string.Empty;
        expires_in = 0;
        raw_data = string.Empty;
    }
}

public class StreamSettingsMessage
{
    public string username { get; set; }
    public string stream_title { get; set; }
    public string chat_welcome_message { get; set; }
    public string[] banned_chat_words { get; set; }
    public bool device_active { get; set; }
    public string photo_url { get; set; }
    public bool live { get; set; }
    public int number_of_followers { get; set; }
    public string channel_id { get; set; }

    public StreamSettingsMessage()
    {
        username = string.Empty;
        stream_title = string.Empty;
        chat_welcome_message = string.Empty;
        banned_chat_words = new string[0];
        device_active = false;
        photo_url = string.Empty;
        live = false;
        number_of_followers = 0;
        channel_id = string.Empty;
    }

    public StreamSettingsMessage(string channelID)
    {
        username = string.Empty;
        stream_title = string.Empty;
        chat_welcome_message = string.Empty;
        banned_chat_words = new string[0];
        device_active = false;
        photo_url = string.Empty;
        live = false;
        number_of_followers = 0;
        channel_id = string.Empty;
    }



}

public class SubscriberMessage
{
    public List<SubscriberItems> items { get; set; }

    public Pagination pagination { get; set; }

    public SubscriberMessage()
    {
        items = new List<SubscriberItems>();
        pagination = new Pagination();
    }

}

public class Pagination
{
    public int total_pages { get; set; }

    public Pagination()
    {
        total_pages = 0;
    }
}

public class SubscriberItems
{
    public string username { get; set; }
    public string expires_at { get; set; }

    public SubscriberItems()
    {
        username = "";
        expires_at = "";
    }
}
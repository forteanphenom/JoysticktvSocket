using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace JoysticktvSocket;

public class JoystickSocketMessage : EventArgs
{
    /// <summary>The JSON exactly as the socket recives it from Joystick.tv.</summary>
    public string rawData { get; init; }
    public MessageType type { get; init; }
    public string type_text { get; init; }
    public string? channelID { get; init; } //id and channelID for the message
    public string? messageID { get; init; }


    public string? user { get; init; }
    public int? tipAmount { get; init; } //used for tips and also wheel spins
    public string? prize { get; init; } //the tip menu item OR the reward from the wheel spin OR a met tip goal
    public DateTime time { get; init; }
    public int? count { get; init; } //viewers, subs, or followers, on an update event
    public DateTime? timerEnds { get; init; }
    public string? timerName { get; init; }
    public string text { get; init; }    // message as it appears in chat
    public List<Emote> emotes { get; init; }

    public string? streamerName { get; init; }


    public bool? isFromStreamer { get; init; }
    public bool? isFromModerator { get; init; }
    public bool? isFromSubscriber { get; init; }
    public bool? isFromHost { get; init; }
    public bool? isFromNew { get; init; }
    public bool? isFromStaff { get; init; }
    public bool? isHighlighted { get; init; }
    public string? usernameColor { get; init; }
    public int? subStreak { get; init; }

    public JoystickSocketMessage()
    {
        type = MessageType.Unknown;
        time = DateTime.Now;
        text = "";
    }
    public JoystickSocketMessage(string message)
    {
        //Console.WriteLine(message);

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

        if (message.StartsWith("{\"type\""))
        {
            type = MessageType.Unknown;
            return;
        }

        Event parsedMessage = JsonSerializer.Deserialize<Event>(message);
        type_text = parsedMessage.message.type;
        type = GetMessageType(parsedMessage.message.type);
        time = parsedMessage.message.occurred_at;
        channelID = parsedMessage.message.channel_id;
        messageID = parsedMessage.message.id;
        text = parsedMessage.message.text;

        if (type == MessageType.ChatMessage)
        {
            user = parsedMessage.message.data.author.username;
            isFromStreamer = parsedMessage.message.data.author.badges.streamer;
            isFromHost = parsedMessage.message.data.author.badges.host;
            isFromModerator = parsedMessage.message.data.author.badges.mod;
            isFromStaff = parsedMessage.message.data.author.badges.staff;
            isFromNew = parsedMessage.message.data.author.badges.@new;
            isFromSubscriber = parsedMessage.message.data.author.badges.subscriber;
            isHighlighted = parsedMessage.message.data.highlight;
            streamerName = parsedMessage.message.data.streamer.username;
            emotes = parsedMessage.message.data.emotes;

            if (isFromSubscriber == true)
                subStreak = parsedMessage.message.data.subscription.streak;

            return;
        }

        user = parsedMessage.message.data.username ?? parsedMessage.message.data.destination_username ?? parsedMessage.message.data.who ?? parsedMessage.message.data.by_user;
        tipAmount = parsedMessage.message.data.amount ?? parsedMessage.message.data.how_much;
        timerEnds = parsedMessage.message.data.ends_at ?? parsedMessage.message.data.expires_at;
        prize = parsedMessage.message.data.prize ?? parsedMessage.message.data.tip_menu_item;
        count = parsedMessage.message.data.number_of_followers ?? parsedMessage.message.data.number_of_viewers ?? parsedMessage.message.data.number_of_subscribers;
        timerName = parsedMessage.message.data.name;
    }

    private static MessageType GetMessageType(string type)
    {
        if (type == "new_message") return MessageType.ChatMessage; // CHECK DOCS. THERE'S LOTS

        if (type == "chat_timer_started") return MessageType.TimerStarted; // name endsAt/ends_at

        if (type == "device_disconnected") return MessageType.DeviceDisconnected;
        if (type == "device_connected") return MessageType.DeviceConnected;
        if (type == "device_settings_updated") return MessageType.DeviceSettingsUpdated; // CHECK DOCS. THERE'S LOTS

        if (type == "ended") return MessageType.StreamEnd; // who

        if (type == "dropin_stream") return MessageType.OutgoingDropIn; // origin number_of_viewers destination_username
        if (type == "followed") return MessageType.NewFollower; // who
        if (type == "follower_count_updated") return MessageType.FollowerCountUpdate; // number_of_followers
        if (type == "gifted_subscriptions") return MessageType.GiftSubs; // who how_much
        if (type == "milestone_completed") return MessageType.MilestoneCompleted; // who title amount
        if (type == "stream_dropped_in") return MessageType.DropIn; // who number_of_viewers
        if (type == "settings_updated") return MessageType.SettingsUpdated; // updated_by
        if (type == "started") return MessageType.StreamStart; // who
        if (type == "subathon_started") return MessageType.BotMessage; //expires_at starting_duration
        if (type == "subathon_ended") return MessageType.BotMessage; // reason banked_seconds
        if (type == "subathon_extended") return MessageType.BotMessage; // who banked source seconds expires_at
        if (type == "wheel_spin_claimed") return MessageType.WheelSpin; // who prize how_much sub_spin

        if (type == "subscribed") return MessageType.NewSubscriber; // who how_much
        if (type == "tip_goal_increased") return MessageType.TipGoalIncreased; // amount by_user current previous
        if (type == "tip_goal_met") return MessageType.TipGoal; // who title amount

        if (type == "tip_goal_updated") return MessageType.TipGoalUpdate; // title amount

        if (type == "tip_menu_item_locked") return MessageType.TipLocked; // title amount
        if (type == "tip_menu_item_unlocked") return MessageType.TipUnlocked; // title amount

        if (type == "tipped") return MessageType.Tip; // who how_much tip_menu_item

        if (type == "viewer_count_updated") return MessageType.ViewerCountUpdate; // number_of_viewers


        if (type == "enter_stream") return MessageType.UserEnter; // who
        if (type == "leave_stream") return MessageType.UserLeave; // who


        // what is below this is not yet documented

        if (type == "stream_mode_updated") return MessageType.StreamModeUpdated;
        if (type == "subscriber_count_updated") return MessageType.SubCountUpdate;
        if (type == "verified_only_chat_started") return MessageType.VerifiedOnlyStarted;
        if (type == "verified_only_chat_ended") return MessageType.VerifiedOnlyEnded;
        if (type == "user_muted") return MessageType.UserMuted;
        if (type == "user_unmuted") return MessageType.UserUnmuted;
        if (type == "pvp_session_requested") return MessageType.PvpRequested;
        if (type == "pvp_session_seady") return MessageType.PvpReady;
        if (type == "pvp_session_started") return MessageType.PvpStarted;
        if (type == "pvp_Session_snded") return MessageType.PvpEnded;
        if (type == "resubscribed") return MessageType.SubRenewed;
        if (type == "bot_message") return MessageType.BotMessage;
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
        result += "\n";
        return result;
    }
}



//private class used to deserialize JSON
public class RawMessages
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
            public string? usernameColor { get; set; }
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


public class Ping
{
    public long? message { get; set; }
}
public class Event
{
    public Message message { get; set; }
}

public class Message
{
    public string type { get; set; }
    public string id { get; set; }
    public string channel_id { get; set; }
    public DateTime occurred_at { get; set; }
    public string text { get; set; }

    public Data data { get; set; }

    public Message()
    {
        type = "undefined";
        id = string.Empty;
        channel_id = string.Empty;
        DateTime occurred_at = DateTime.MinValue;
        text = string.Empty;

        data = new Data();
    }

}

public class Data
{
    public string? name { get; set; }
    public DateTime? ends_at { get; set; }
    public string? who { get; set; }
    public string? by_user { get; set; }
    public string? origin { get; set; }
    public string? destination_username { get; set; }
    public int? number_of_viewers { get; set; }
    public int? number_of_followers { get; set; }
    public int? number_of_subscribers { get; set; }
    public int? how_much { get; set; }
    public string? title { get; set; }
    public int? amount { get; set; }
    public string? tip_menu_item { get; set; }
    public string? prize { get; set; }
    public DateTime? expires_at { get; set; }

    public Author? author { get; set; }
    public Streamer? streamer { get; set; }
    public Subscription subscription { get; set; }
    public string? text { get; set; }
    public bool? highlight { get; set; }
    public List<string>? mentions { get; set; }
    public List<Emote>? emotes { get; set; }
    public string username { get; set; }

}

public class Author
{
    public string? username { get; set; }
    public string? nickname { get; set; }
    public string? color { get; set; }
    public Badges? badges { get; set; }
}

public class Subscription
{
    public int streak { get; set; }
}

public class Streamer
{
    public string username { get; set; }
}

public class Emote
{
    public string code { get; set; }
    public string url { get; set; }
}

public class Badges
{
    public bool streamer { get; set; }
    public bool mod { get; set; }
    public bool subscriber { get; set; }
    public bool @new { get; set; }
    public bool host { get; set; }
    public bool staff { get; set; }

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

    public SubscriberItems()
    {
        username = "";
    }
}

public class SocketSubscribeMessage
{
    public string command { get; set; }
    public string identifier { get; set; }

    public SocketSubscribeMessage()
    {
        command = "subscribe";
        identifier = "{\"channel\":\"GatewayChannel\",\"event_version\":\"v2\"}";
    }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this);
    }
}

public class SocketCommandMessage
{
    public string command { get; set; }
    public string identifier { get; set; }
    public string data { get; set; }

    public SocketCommandMessage(object data)
    {
        command = "message";
        identifier = "{\"channel\":\"GatewayChannel\",\"event_version\":\"v2\"}";
        this.data = JsonSerializer.Serialize(data);
    }

    public string Serialize()
    {

        var serializeOptions = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        
        return JsonSerializer.Serialize(this, serializeOptions);
    }

}

public class CommmandData
{
    public string action { get; set; }
    public string text { get; set; }
    public string channel_id { get; set; }
    public string message_id { get; set; }
    public string username { get; set; }

    public CommmandData(string action, string username, string text, string channelID, string messageID)
    {
        this.action = action;
        this.text = text;
        channel_id = channelID;
        message_id = messageID;
        this.username = username;
    }
}
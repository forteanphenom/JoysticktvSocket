using System;
using System.Text.Json;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace JoysticktvSocket;


//the class that contains our websocket, whose methods we will use to send and receive messages
public class JoystickConnection
{
    private ClientWebSocket ws;
    private Thread listenerThread;
    private CancellationTokenSource canceller;

    private string _basicKey;

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

    public JoystickConnection(string clientID, string clientSecret)
    {
        byte[] plainTextBytes = Encoding.UTF8.GetBytes($"{clientID}:{clientSecret}");
        _basicKey = Convert.ToBase64String(plainTextBytes); ;

    }

    /// <summary>
    /// Connects synchronoursly to Joystick.tv's Websocket 
    /// </summary>
    /// <param name="clientID"></param>
    /// <param name="clientSecret"></param>
    /// <returns></returns>
    public JoystickWebsocketStatus Connect()
    {
        //set up my cancelation token
        canceller = new CancellationTokenSource();

        Uri joystuckUri = new($"wss://joystick.tv/cable?token={_basicKey}"); //make me a URI with my id and secret

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
    public void ConnectAndListen()
    {
        listenerThread = new Thread(() => Listen());
        listenerThread.Start();
    }

    private void Listen()
    {
        if (TroubleshootOutput) Console.WriteLine("Connecting.");
        Connect();
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
                JoystickWebsocketStatus status = Connect();
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

    private static string EscapeCharacters(string message)
    {
        for (int i = message.Length - 1; i >= 0; i--)
            if (message[i] == '"' || message[i] == '\\') message = message.Insert(i, "\\\\\\");
        return message;
    }

}
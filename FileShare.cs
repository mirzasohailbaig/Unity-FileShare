using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Sending files through network on the background with TCP
/// Using Unity HLAPI and Threading
/// </summary>
public sealed class FileShare : MonoBehaviour
{
    public int portRangeFrom = 47100;
    public int portRangeTo = 47200;
    public int bufferSize = 1024;
    public int timeout = 10;

    #region network message stuff
    const short fileSharePrepare = MsgType.Highest + 1;
    const short getClientsSendFile = MsgType.Highest + 2;

    class FileSharePrepare : MessageBase
    {
        public string crc;
        public string receiveAction;
        public string extension;
        public int port;
    }

    class NetConn : MessageBase
    {
        public string[] addresses;
        public int port;
        public string file;
    }
    #endregion

    #region variables
    static FileShare instance;
    static List<int> ports = new List<int>();
    static System.Random r;
    //List of all received files, key is crc
    static Dictionary<string, FileData> files = new Dictionary<string, FileData>();
    //actions after receiving file, key is custom identifier
    static Dictionary<string, Action<string>> onReceiveActions = new Dictionary<string, Action<string>>();

    struct FileData
    {
        public string file;
        public string receiveAction;
    }
    #endregion

    private void Start()
    {
        instance = this;
        r = new System.Random();
        StartCoroutine(RegisterHandlers());
    }

    IEnumerator RegisterHandlers()
    {
        yield return new WaitUntil(() => NetworkManager.singleton.isNetworkActive);

        if (NetworkServer.active)
        {
            NetworkServer.RegisterHandler(fileSharePrepare, ServerPrepare);
            NetworkServer.RegisterHandler(getClientsSendFile, ServerConnections);
        }

        if (NetworkClient.active)
        {
            NetworkManager.singleton.client.RegisterHandler(fileSharePrepare, ClientPrepare);
            NetworkManager.singleton.client.RegisterHandler(getClientsSendFile, SendFileToClients);
        }
    }

    private void ServerPrepare(NetworkMessage netMsg)
    {
        FileSharePrepare msg = netMsg.ReadMessage<FileSharePrepare>();
        NetworkServer.SendToAll(netMsg.msgType, msg);
    }

    private void ServerConnections(NetworkMessage netMsg)
    {
        NetConn msg = netMsg.ReadMessage<NetConn>();
        List<string> addresses = new List<string>();
        foreach (var conn in NetworkServer.connections)
        {
            if (conn != null)
                addresses.Add(conn.address);
        }
        msg.addresses = addresses.ToArray();

        NetworkServer.SendToClient(netMsg.conn.connectionId, getClientsSendFile, msg);
    }

    private void ClientPrepare(NetworkMessage netMsg)
    {
        FileSharePrepare msg = netMsg.ReadMessage<FileSharePrepare>();

        if (files.ContainsKey(msg.crc))
        {
            if (onReceiveActions.ContainsKey(msg.receiveAction))
                onReceiveActions[msg.receiveAction](files[msg.crc].file);
        }
        else
        {
            StartCoroutine(WaitForReceivedFile(msg.crc));
            new Thread(() => Host(msg.extension, msg.receiveAction, msg.port)).Start();
        }
    }

    private void SendFileToClients(NetworkMessage netMsg)
    {
        NetConn msg = netMsg.ReadMessage<NetConn>();

        foreach (var addr in msg.addresses)
        {
            //initialize sender
            new Thread(() => Client(addr.Replace("::ffff:", ""), msg.port, msg.file)).Start();
        }
    }

    IEnumerator WaitForReceivedFile(string crc)
    {
        //the reason why I used while: WaitUntil isn't working with this condition
        while (!files.ContainsKey(crc))
            yield return null;

        if (onReceiveActions.ContainsKey(files[crc].receiveAction))
            onReceiveActions[files[crc].receiveAction](files[crc].file);
    }

    public static void RegisterReceiveAction(string name, Action<string> action)
    {
        onReceiveActions[name] = action;
    }

    public static void UnregisterReceiveAction(string name)
    {
        if (onReceiveActions.ContainsKey(name))
            onReceiveActions.Remove(name);
    }

    /// <summary>
    /// Send file
    /// </summary>
    /// <param name="file">path to file</param>
    /// <param name="receiveAction">Action identifier after receiving a file</param>
    public static void Send(string file, string receiveAction)
    {
        if (ports.Count >= (instance.portRangeTo - instance.portRangeFrom))
            throw new Exception("Not available port");

        int port = instance.portRangeFrom;
        while (ports.Contains(port) && port < instance.portRangeTo)
            port++;
        ports.Add(port);

        //msg to clients, to be prepared
        NetworkManager.singleton.client.Send(fileSharePrepare, new FileSharePrepare()
        {
            crc = GetCRC(file),
            receiveAction = receiveAction,
            extension = Path.GetExtension(file),
            port = port
        });

        NetworkManager.singleton.client.Send(getClientsSendFile, new NetConn()
        {
            port = port,
            file = file
        });
    }

    /// <summary>
    /// Receiving file
    /// </summary>
    /// <param name="extension"></param>
    /// <param name="receiveAction"></param>
    static void Host(string extension, string receiveAction, int port)
    {
        try
        {
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            TcpClient client = listener.AcceptTcpClient();

            var netstream = client.GetStream();
            byte[] recData = new byte[instance.bufferSize];
            int totalrecbytes = 0;
            int recBytes;

            string name = DateTime.Now.ToString("yyyyMMddHHmmss_") + r.Next(1000, 10000);
            //received files are stored in temp
            string tmpFile = Path.GetTempPath() + name + ".tmp";
            string normalFile = Path.GetTempPath() + name + extension;

            FileStream fs = new FileStream(tmpFile, FileMode.Create, FileAccess.Write);
            while ((recBytes = netstream.Read(recData, 0, recData.Length)) > 0)
            {
                fs.Write(recData, 0, recBytes);
                totalrecbytes += recBytes;
            }
            fs.Close();

            netstream.Close();
            client.Close();
            listener.Stop();

            if (File.Exists(normalFile))
                File.Delete(normalFile);
            File.Move(tmpFile, normalFile);

            files.Add(GetCRC(normalFile), new FileData() { file = normalFile, receiveAction = receiveAction });
        }
        catch (Exception e)
        {
            File.AppendAllText("fileshare.log", "[" + DateTime.Now.ToString() + "] " + e.Source + ": " + e.Message + "\n" + e.StackTrace + "\n\n");
        }
    }

    /// <summary>
    /// Sending file over TCP
    /// </summary>
    /// <param name="ip">Where</param>
    /// <param name="port"></param>
    /// <param name="path">What</param>
    static void Client(string ip, int port, string path)
    {
        Stopwatch sw = new Stopwatch();
        sw.Start();

        TcpClient client;
        while (true)
        {
            try
            {
                client = new TcpClient(ip, port);
                if (client.Connected)
                    break;
            }
            catch (Exception)
            {
                if (sw.Elapsed.Seconds > instance.timeout)
                {
                    sw.Stop();
                    return;
                }

                continue;
            };
        }

        sw.Stop();

        try
        {
            NetworkStream netstream = client.GetStream();

            byte[] file = File.ReadAllBytes(path);
            netstream.Write(file, 0, file.Length);
            netstream.Close();

            client.Close();
            ports.Remove(port);
        }
        catch (Exception e)
        {
            File.AppendAllText("fileshare.log", "[" + DateTime.Now.ToString() + "] " + e.Source + ": " + e.Message + "\n" + e.StackTrace + "\n\n");
        }
    }

    /// <summary>
    /// CRC is generated only from first [bufferSize] bytes, because of performance
    /// </summary>
    /// <param name="file"></param>
    /// <returns>CRC</returns>
    static string GetCRC(string file)
    {
        byte[] data = new byte[instance.bufferSize];

        var stream = File.OpenRead(file);
        stream.Read(data, 0, (int)Mathf.Clamp(instance.bufferSize, 0, new FileInfo(file).Length));
        stream.Close();

        MD5 md5 = MD5.Create();
        return System.Text.Encoding.UTF8.GetString(md5.ComputeHash(data));
    }

    /// <summary>
    /// Delete all received files from temp
    /// </summary>
    private void OnDestroy()
    {
        foreach (var f in files)
        {
            if (File.Exists(f.Value.file))
                File.Delete(f.Value.file);
        }
    }
}

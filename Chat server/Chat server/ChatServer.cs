using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Stormancer;
using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer.Server.Components;

public struct ChatUserInfo
{
    public long ClientId;
    public string User;
}

public struct ChatMessageDTO
{
    public ChatUserInfo UserInfo;
    public string Message;
    public long TimeStamp;
}

public static class ChatServerExtensions
{
    public static ChatServer AddChat(this ISceneHost scene)
    {
        return new ChatServer(scene);
    }
}

public class ChatServerRun
{
    void Run(IAppBuilder builder)
    {
        builder.SceneTemplate("ChatRoom", scene => scene.AddChat());
    }
}

public class ChatServer
{
    private ISceneHost _scene;
    private IEnvironment _env;
    private ConcurrentDictionary<long, ChatUserInfo> _UsersInfos = new ConcurrentDictionary<long, ChatUserInfo>();

    //_NbrMessagesToKeep messages are kept in memory. Older ones are deleted when new messages kicks in.
    private ConcurrentQueue<ChatMessageDTO> _MessagesCache = new ConcurrentQueue<ChatMessageDTO>();
    private long _NbrMessagesToKeep = 100;

    void OnMessageReceived(Packet<IScenePeerClient> packet)
    {
        var dto = new ChatMessageDTO();
        ChatUserInfo temp;

        if (_UsersInfos.TryGetValue(packet.Connection.Id, out temp) == false)
        {
            temp = new ChatUserInfo();
            temp.ClientId = packet.Connection.Id;
            temp.User = "";
        }
        dto.UserInfo = temp;
        dto.Message = packet.ReadObject<string>();
        dto.TimeStamp = _env.Clock;

        AddMessageToCache(dto);

        _scene.Broadcast("chat", dto, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
    }

    void AddMessageToCache(ChatMessageDTO dto)
    {
        _MessagesCache.Enqueue(dto);

        ChatMessageDTO trash;
        while (_MessagesCache.Count > _NbrMessagesToKeep)
        {
            _MessagesCache.TryDequeue(out trash);
        }
    }

    void OnUpdateInfo(Packet<IScenePeerClient> packet)
    {
        var info = packet.ReadObject<ChatUserInfo>();
        if (_UsersInfos.ContainsKey(packet.Connection.Id) == true)
        {
            ChatUserInfo trash;
            _UsersInfos.TryRemove(packet.Connection.Id, out trash);
        }
        info.ClientId = packet.Connection.Id;
        _UsersInfos.TryAdd(packet.Connection.Id, info);
        _scene.Broadcast<ChatUserInfo>("UpdateInfo", info);
    }

    Task OnConnected(IScenePeerClient client)
    {
        List<ChatMessageDTO> messages = _MessagesCache.ToList();
        int i = messages.Count - 1;

        while (i >= 0)
        {
            client.Send<ChatMessageDTO>("chat", messages[i]);
            i--;
        }
        return Task.FromResult(true);
    }

    Task OnDisconnected(DisconnectedArgs args)
    {
        if (_UsersInfos.ContainsKey(args.Peer.Id) == true)
        {
            ChatUserInfo temp;
            _UsersInfos.TryRemove(args.Peer.Id, out temp);

            ChatMessageDTO dto = new ChatMessageDTO();
            dto.UserInfo = temp;
            dto.Message = args.Reason;
            _scene.Broadcast<ChatMessageDTO>("DiscardInfo", dto, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
        }
        return Task.FromResult(true);
    }

    Task OnGetUsersInfos(RequestContext<IScenePeerClient> ctx)
    {
        var users = new List<ChatUserInfo>();

        foreach(ChatUserInfo user in _UsersInfos.Values)
        {
            users.Add(user);
        }

        ctx.SendValue<List<ChatUserInfo>>(users);

        return Task.FromResult(true);
    }

    public ChatServer(ISceneHost scene)
    {
        _scene = scene;
        _env = _scene.GetComponent<IEnvironment>();
        _scene.AddProcedure("GetUsersInfos", OnGetUsersInfos);
        _scene.AddRoute("UpdateInfo", OnUpdateInfo);
        _scene.AddRoute("chat", OnMessageReceived);
        _scene.Connected.Add(OnConnected);
        _scene.Disconnected.Add(OnDisconnected);
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Stormancer;
using Stormancer.Core;
using Stormancer.Plugins;
using Stormancer.Server;

public struct ChatUserInfo
{
    public long ClientId;
    public string User;
}

public struct ChatMessageDTO
{
    public ChatUserInfo UserInfo;
    public string Message;
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
    private ConcurrentDictionary<long, ChatUserInfo> UsersInfos = new ConcurrentDictionary<long, ChatUserInfo>();

    void OnMessageReceived(Packet<IScenePeerClient> packet)
    {
        var dto = new ChatMessageDTO();
        ChatUserInfo temp;

        if (UsersInfos.TryGetValue(packet.Connection.Id, out temp) == false)
        {
            temp = new ChatUserInfo();
            temp.ClientId = packet.Connection.Id;
            temp.User = "";
        }
        dto.UserInfo = temp;
        dto.Message = packet.ReadObject<string>();
        _scene.Broadcast("chat", dto, PacketPriority.MEDIUM_PRIORITY, PacketReliability.RELIABLE);
    }

    void OnUpdateInfo(Packet<IScenePeerClient> packet)
    {
        var info = packet.ReadObject<ChatUserInfo>();
        if (UsersInfos.ContainsKey(packet.Connection.Id) == true)
        {
            ChatUserInfo trash;
            UsersInfos.TryRemove(packet.Connection.Id, out trash);
        }
        info.ClientId = packet.Connection.Id;
        UsersInfos.TryAdd(packet.Connection.Id, info);
        _scene.Broadcast<ChatUserInfo>("UpdateInfo", info);
    }

    Task OnDisconnected(DisconnectedArgs args)
    {
        if (UsersInfos.ContainsKey(args.Peer.Id) == true)
        {
            ChatUserInfo temp;
            UsersInfos.TryRemove(args.Peer.Id, out temp);

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

        foreach(ChatUserInfo user in UsersInfos.Values)
        {
            users.Add(user);
        }

        ctx.SendValue<List<ChatUserInfo>>(users);

        return Task.FromResult(true);
    }

    public ChatServer(ISceneHost scene)
    {
        _scene = scene;
        _scene.AddProcedure("GetUsersInfos", OnGetUsersInfos);
        _scene.AddRoute("UpdateInfo", OnUpdateInfo);
        _scene.AddRoute("chat", OnMessageReceived);
        _scene.Disconnected.Add(OnDisconnected);
    }
}

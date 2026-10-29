using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
namespace KK_StoryNPCBridge {
internal sealed class Request {public Session Session;public Dictionary<string,object> Data;public DateTime Received;}
internal sealed class Session {
 public readonly string Id=Guid.NewGuid().ToString("N");
 public volatile bool Alive=true,Authenticated,Subscribed;
 public long LastSeen=DateTime.UtcNow.Ticks;
 public TcpClient Client;
 readonly Queue<string> replies=new Queue<string>();
 public void Reply(object message){string json=Json.Stringify(message);lock(replies){if(replies.Count>=64){Alive=false;return;}replies.Enqueue(json);}}
 public string Pop(){lock(replies)return replies.Count==0?null:replies.Dequeue();}
}
internal sealed class BridgeServer:IDisposable {
 readonly TcpListener listener;readonly string token;
 readonly List<Session> sessions=new List<Session>();readonly Queue<Request> requests=new Queue<Request>();
 volatile bool running;public volatile string Snapshot;
 public BridgeServer(int port,string secret){listener=new TcpListener(IPAddress.Loopback,port);token=secret;}
 public void Start(){listener.Start(4);running=true;var t=new Thread(Accept);t.IsBackground=true;t.Name="KK_StoryNPCBridge.Accept";t.Start();}
 public int ClientCount {get{lock(sessions)return sessions.FindAll(delegate(Session s){return s.Alive&&s.Authenticated;}).Count;}}
 public Request Pop(){lock(requests)return requests.Count==0?null:requests.Dequeue();}
 void Accept(){while(running){try{
  TcpClient c=listener.AcceptTcpClient();c.NoDelay=true;c.ReceiveTimeout=2000;c.SendTimeout=2000;var s=new Session();s.Client=c;
  lock(sessions){sessions.RemoveAll(delegate(Session x){return !x.Alive;});if(sessions.Count>=4){c.Close();continue;}sessions.Add(s);}
  var t=new Thread(delegate(){Serve(s);});t.IsBackground=true;t.Name="KK_StoryNPCBridge.Client";t.Start();
 }catch(SocketException){if(running)Thread.Sleep(50);}catch(ObjectDisposedException){break;}}}
 void Serve(Session s){try{
  NetworkStream stream=s.Client.GetStream();var encoding=new UTF8Encoding(false,true);var pending=new List<byte>();var buffer=new byte[4096];string last=null;
  while(running&&s.Alive){
   if((DateTime.UtcNow.Ticks-Interlocked.Read(ref s.LastSeen))/TimeSpan.TicksPerSecond>(s.Authenticated?30:5))break;
   if(s.Client.Client.Poll(0,SelectMode.SelectRead)&&s.Client.Available==0)break;
   if(stream.DataAvailable){int count=stream.Read(buffer,0,buffer.Length);if(count==0)break;for(int i=0;i<count;i++){
    if(buffer[i]==10){Handle(s,encoding.GetString(pending.ToArray()));pending.Clear();if(!s.Alive)break;}
    else{pending.Add(buffer[i]);if(pending.Count>65536)throw new IOException("Frame too large");}
   }}
   string reply;while((reply=s.Pop())!=null)Send(stream,reply);
   string snapshot=Snapshot;if(s.Authenticated&&s.Subscribed&&snapshot!=null&&snapshot!=last){Send(stream,"{\"event\":\"snapshot\",\"data\":"+snapshot+"}");last=snapshot;}
   Thread.Sleep(15);
  }
 }catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}catch(DecoderFallbackException){}
 finally{s.Alive=false;s.Client.Close();}}
 static void Send(NetworkStream s,string json){byte[] b=Encoding.UTF8.GetBytes(json+"\n");s.Write(b,0,b.Length);}
 void Handle(Session s,string line){object id=null;try{
  var d=Json.Parse(line) as Dictionary<string,object>;if(d==null)throw new FormatException("JSON object required");d.TryGetValue("id",out id);
  if(!(id is string)||((string)id).Length<1||((string)id).Length>64)throw new FormatException("id must be a short string");
  object operation;if(!d.TryGetValue("op",out operation)||!(operation is string))throw new FormatException("op string required");string op=(string)operation;
  if(!s.Authenticated){object secret;if(op!="auth"||!d.TryGetValue("token",out secret)||!(secret is string)||!Equal((string)secret,token)){s.Reply(Error(id,"unauthorized","Authentication required"));return;}
   s.Authenticated=true;s.Reply(Ok(id,Json.Map("pluginName",Plugin.PluginName,"pluginGuid",Plugin.PluginGuid,"protocol",Plugin.Protocol,"version",Plugin.Version,"session",s.Id)));
  }else if(op=="ping")s.Reply(Ok(id,Json.Map("pong",true)));
  else if(op=="subscribe"){s.Subscribed=true;s.Reply(Ok(id,Json.Map("subscribed",true)));}
  else{lock(requests){if(requests.Count>=64){s.Reply(Error(id,"busy","Command queue full"));return;}requests.Enqueue(new Request{Session=s,Data=d,Received=DateTime.UtcNow});}}
  Interlocked.Exchange(ref s.LastSeen,DateTime.UtcNow.Ticks);
 }catch(FormatException e){s.Reply(Error(id,"invalid_request",e.Message));}}
 static bool Equal(string a,string b){int x=a.Length^b.Length;for(int i=0;i<b.Length;i++)x|=b[i]^(i<a.Length?a[i]:0);return x==0;}
 public static object Ok(object id,object value){return Json.Map("id",id,"ok",true,"result",value);}
 public static object Error(object id,string code,string message){return Json.Map("id",id,"ok",false,"error",Json.Map("code",code,"message",message));}
 public void Dispose(){running=false;listener.Stop();lock(sessions)foreach(Session s in sessions){s.Alive=false;s.Client.Close();}}
}
}

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Speech.Synthesis;
using System.Windows.Forms;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Rech
{
  public class App
  {
    [STAThread]
    public static void Main()
    {
      try
      {
        Run();
      }
      catch (Exception ex)
      {
        WriteCrash(ex);
        MessageBox.Show(ex.ToString(), "РЕЧЬ");
      }
    }

    static void WriteCrash(Exception ex)
    {
      try
      {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rech");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "last-error.txt"), ex.ToString(), Encoding.UTF8);
      }
      catch
      {
      }
    }

    static void Run()
    {
      bool created;
      Mutex mutex = new Mutex(true, "Local\\RechStudio", out created);
      if (!created)
      {
        MessageBox.Show("РЕЧЬ уже запущена. Значок в трее рядом с часами.", "РЕЧЬ");
        return;
      }
      try
      {
        SetProcessDPIAware();
      }
      catch
      {
      }
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
      Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs args)
      {
        WriteCrash(args.Exception);
        MessageBox.Show(args.Exception.Message, "РЕЧЬ");
      };
      Application.Run(new MainForm());
      GC.KeepAlive(mutex);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();
  }

  public class ChatMessage
  {
    public string Id;
    public string SourceId;
    public string Platform;
    public string Author;
    public string Text;
    public long Ts;
    public bool Fresh;
    public string Icons;
  }

  static class Swear
  {
    static readonly Regex Rus;
    static readonly Regex Lat;
    const string Words = "пидорас пидор пидрила пидарас пидар педераст педрила педик гомосек гомосятина гомик лесбуха куколд инцел девственница девственник лошара лошок лохи лоху лоха лох геи гей чурка чурек хачик хохол салоэд москаль кацап русня узкоглазый китаеза китаёза чучмек черномазый черножопый ниггер нигер нигга негр даун дцпшник аутист дебилка дебил калека кретин имбецил олигофрен ретард инвалид шалава шлава шлюха шмара шкура сучка суки суку сука охуительно охуенно охуел охуеть хуесос хуйня хуем хуею хуям хуях хуёвый хуета похуй нахуй хуйи хуя хуе хуи хуй распиздяй пиздатый пиздеть пиздец пизду пизде пизда долбоеб ебанутый ебаный заебись заебал проебал наебал уебан уебок еблан ебло ебать ебал ебет ебу блядина блядь блять бля мудила мудак мудень гандон гондон залупа вскройся повесься убейся сдохни суицид жиды жидов жид даг";

    static Swear()
    {
      string[] list = Words.Split(' ');
      System.Array.Sort(list, delegate(string a, string b) { return b.Length - a.Length; });
      StringBuilder pattern = new StringBuilder();
      int i;
      for (i = 0; i < list.Length; i++)
      {
        if (list[i].Length == 0) continue;
        if (pattern.Length > 0) pattern.Append('|');
        pattern.Append(Flex(list[i]));
      }
      Rus = new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
      Lat = new Regex("(?<![a-z])(?:nigg(?:er|a)s?|fagg?ots?|fags?|retards?|incels?|simps?|virgins?|cuck(?:old)?s?|whores?|sluts?|kys)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public static string Apply(string text, bool on)
    {
      if (!on || text == null || text.Length == 0) return text == null ? "" : text;
      string next = Rus.Replace(text, "пип");
      next = Lat.Replace(next, "пип");
      next = Regex.Replace(next, "(?:пип)(?:\\s+пип)+", "пип");
      return Regex.Replace(next, "\\s+", " ").Trim();
    }

    static string Flex(string word)
    {
      string stem = word.Replace('ё', 'е').Replace('Ё', 'е');
      StringBuilder pattern = new StringBuilder();
      pattern.Append("(?<![а-яёa-z0-9])");
      int i;
      for (i = 0; i < stem.Length; i++)
      {
        char c = stem[i];
        if (c == 'ь' || c == 'ъ')
        {
          pattern.Append("ь?");
          continue;
        }
        if (i > 0) pattern.Append("[^а-яёa-z0-9]{0,2}");
        pattern.Append(Class(c));
        pattern.Append("{1,2}");
      }
      if (stem.Length >= 4) pattern.Append("[а-яё]{0,3}");
      pattern.Append("(?![а-яёa-z0-9])");
      return pattern.ToString();
    }

    static string Class(char c)
    {
      if (c == 'а') return "[аa@4]";
      if (c == 'б') return "[б6b]";
      if (c == 'в') return "[вb]";
      if (c == 'г') return "[гg]";
      if (c == 'д') return "[дd]";
      if (c == 'е') return "[еёe3]";
      if (c == 'з') return "[з3z]";
      if (c == 'и') return "[иi1!*]";
      if (c == 'й') return "[й]";
      if (c == 'к') return "[кk]";
      if (c == 'л') return "[лl]";
      if (c == 'м') return "[мm]";
      if (c == 'н') return "[нh]";
      if (c == 'о') return "[оo0]";
      if (c == 'п') return "[пn]";
      if (c == 'р') return "[рp]";
      if (c == 'с') return "[сc]";
      if (c == 'т') return "[тt]";
      if (c == 'у') return "[уy*]";
      if (c == 'х') return "[хx]";
      if (c == 'ч') return "[ч]";
      if (c == 'ш') return "[ш]";
      if (c == 'щ') return "[щ]";
      if (c == 'ю') return "[ю@*]";
      if (c == 'я') return "[я*]";
      return Regex.Escape(c.ToString());
    }
  }

  public class SourceConfig
  {
    public string Id;
    public string Platform;
    public string Input;
    public bool Enabled;
    public string Voice;
  }

  public class Settings
  {
    public List<SourceConfig> Sources;
    public bool TtsEnabled;
    public string Engine;
    public bool ReadAuthor;
    public int Volume;
    public int RatePercent;
    public string VoiceName;
    public string SileroUrl;
    public string SileroOutputs;
    public string SileroSpeaker;
    public bool SkipEmotes;
    public bool? ReadEnglish;
    public bool? ReadNumbers;
    public string MuteUsers;
    public bool ShowPlatform;
    public bool ShowTime;
    public bool AlwaysOnTop;
    public bool Rehearsal;
    public int Version;
    public bool Chroma;
    public int WinX;
    public int WinY;
    public int WinW;
    public int WinH;
    public string OverlayFont;
    public int OverlaySize;
    public bool OverlayBold;
    public string OverlayColor;
    public string OverlayNick;
    public string OverlayStroke;
    public bool OverlayPlate;
    public string OverlayAlign;
    public int OverlayLines;
    public int OverlayHold;
    public int ChatX;
    public int ChatY;
    public int ChatW;
    public int ChatH;
    public string ReaderFont;
    public int ReaderSize;
    public string ReaderColor;
    public string ReaderNick;
    public string ReaderBack;
    public string GameHotkey;
    public bool? GameDeleteAudio;
    public bool? GameTranslate;
    public bool? GameSpeakLater;
    public bool? GameHotkeyOn;
    public bool? GameKeepGame;
    public bool? HideSwear;
    public bool? GamePicture;
    public int SpeechDelay;
    public string NickMap;
    public string PauseHotkey;
    public bool? PauseHotkeyOn;
    public int? GamePhraseSec;
    public bool? LucyOn;
    public string LucyFacts;
    public string TwitchClientId;
    public string TwitchToken;
    public string TwitchRefresh;
    public string TwitchBotToken;
    public string TwitchBotRefresh;
    public string TwitchBotLogin;
    public string SongReward;
    public string LucyReward;
    public int SongSeconds;
    public int? SongVolume;
    public bool? SongYoutube;
    public bool? SongRutube;
    public bool? SongVk;
  }

  public class VoicePrefs
  {
    public bool Enabled;
    public string Engine;
    public string VoiceName;
    public string SileroUrl;
    public string SileroOutputs;
    public string SileroSpeaker;
    public int Volume;
    public int RatePercent;
    public bool ReadAuthor;
    public bool SkipEmotes;
  }

  public class HaltException : Exception
  {
    public int RetryMs;
    public HaltException(string message, int retryMs) : base(message)
    {
      RetryMs = retryMs;
    }
  }

  public class Row
  {
    public string Mark;
    public string Platform;
    public string Author;
    public string Text;
    public string Time;
    public string Icons;
  }

  public class Net
  {
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
    public static readonly HttpClient Http = Create();

    static Net()
    {
      try
      {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
      }
      catch
      {
      }
      ServicePointManager.Expect100Continue = false;
    }

    static HttpClient Create()
    {
      HttpClientHandler handler = new HttpClientHandler();
      handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
      HttpClient client = new HttpClient(handler);
      client.Timeout = TimeSpan.FromSeconds(30);
      client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
      return client;
    }

    public static object Box(string json)
    {
      if (json == null || json.Trim().Length == 0) return null;
      return Box(JToken.Parse(json));
    }

    static object Box(JToken token)
    {
      if (token == null || token.Type == JTokenType.Null) return null;
      JObject obj = token as JObject;
      if (obj != null)
      {
        Dictionary<string, object> dict = new Dictionary<string, object>();
        foreach (JProperty prop in obj.Properties()) dict[prop.Name] = Box(prop.Value);
        return dict;
      }
      JArray arr = token as JArray;
      if (arr != null)
      {
        object[] list = new object[arr.Count];
        int i;
        for (i = 0; i < arr.Count; i++) list[i] = Box(arr[i]);
        return list;
      }
      JValue value = token as JValue;
      if (value != null) return value.Value;
      return token.ToString();
    }

    public static Dictionary<string, object> Parse(string json)
    {
      return Box(json) as Dictionary<string, object>;
    }

    public static string Serialize(object value)
    {
      return JsonConvert.SerializeObject(value);
    }

    public static T Read<T>(string json)
    {
      return JsonConvert.DeserializeObject<T>(json);
    }

    public static async Task<string> GetText(string url, CancellationToken ct)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      req.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
      req.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI");
      HttpResponseMessage res = await Http.SendAsync(req, ct);
      string body = await res.Content.ReadAsStringAsync();
      if (!res.IsSuccessStatusCode)
      {
        throw new Exception("Ответ " + ((int)res.StatusCode).ToString(CultureInfo.InvariantCulture));
      }
      return body;
    }

    public static async Task<string> PostJson(string url, string json, CancellationToken ct)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
      req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
      req.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI");
      req.Content = new StringContent(json, Encoding.UTF8, "application/json");
      HttpResponseMessage res = await Http.SendAsync(req, ct);
      string body = await res.Content.ReadAsStringAsync();
      if (!res.IsSuccessStatusCode)
      {
        throw new Exception("Ответ " + ((int)res.StatusCode).ToString(CultureInfo.InvariantCulture));
      }
      return body;
    }

    public static Dictionary<string, object> Dict(Dictionary<string, object> parent, string key)
    {
      if (parent == null || !parent.ContainsKey(key)) return null;
      return parent[key] as Dictionary<string, object>;
    }

    public static IList List(Dictionary<string, object> parent, string key)
    {
      if (parent == null || !parent.ContainsKey(key)) return null;
      return parent[key] as IList;
    }

    public static object Get(Dictionary<string, object> parent, string key)
    {
      if (parent == null || !parent.ContainsKey(key)) return null;
      return parent[key];
    }

    public static string S(object value)
    {
      if (value == null) return "";
      return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static string Clean(string value)
    {
      if (value == null) return "";
      value = Regex.Replace(value, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
      value = Regex.Replace(value, "<[^>]+>", "");
      value = WebUtility.HtmlDecode(value);
      value = Regex.Replace(value, "\\s+", " ").Trim();
      if (value.Length > 500) value = value.Substring(0, 500);
      return value;
    }

    public static long ToMs(object value)
    {
      if (value == null) return Now();
      string text = Convert.ToString(value, CultureInfo.InvariantCulture);
      if (text == null) return Now();
      double number;
      if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && number > 0)
      {
        if (number < 10000000000d) return (long)(number * 1000d);
        return (long)number;
      }
      DateTime dt;
      if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dt))
      {
        return (long)(dt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
      }
      return Now();
    }

    public static long Now()
    {
      return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }

    public static string Match(string input, string pattern)
    {
      if (input == null) return null;
      Match found = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
      if (!found.Success) return null;
      return found.Groups[1].Value;
    }
  }

  public class WsClient : IDisposable
  {
    ClientWebSocket socket;

    public async Task Connect(string url, CancellationToken ct, string origin)
    {
      socket = new ClientWebSocket();
      socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
      if (origin != null && origin.Length > 0)
      {
        socket.Options.SetRequestHeader("Origin", origin);
      }
      await socket.ConnectAsync(new Uri(url), ct);
    }

    public Task Send(string text, CancellationToken ct)
    {
      byte[] bytes = Encoding.UTF8.GetBytes(text);
      return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public async Task<string> Receive(CancellationToken ct)
    {
      MemoryStream ms = new MemoryStream();
      byte[] buf = new byte[16384];
      while (true)
      {
        WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buf), ct);
        if (result.MessageType == WebSocketMessageType.Close)
        {
          throw new Exception("соединение закрыто");
        }
        ms.Write(buf, 0, result.Count);
        if (result.EndOfMessage) break;
      }
      return Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Abort()
    {
      try
      {
        if (socket != null) socket.Abort();
      }
      catch
      {
      }
    }

    public void Dispose()
    {
      Abort();
      try
      {
        if (socket != null) socket.Dispose();
      }
      catch
      {
      }
    }
  }

  public class FeedEngine
  {
    public delegate void StatusHandler(string id, string state, string detail);
    public delegate void MessageHandler(ChatMessage message);
    public event StatusHandler Status;
    public event MessageHandler Message;

    int generation;
    readonly object gate = new object();
    readonly Dictionary<string, CancellationTokenSource> gates = new Dictionary<string, CancellationTokenSource>();
    static string vkToken;
    static long vkTokenExp;

    public void Restart(List<SourceConfig> sources)
    {
      generation = generation + 1;
      int gen = generation;
      List<CancellationTokenSource> old = new List<CancellationTokenSource>();
      lock (gate)
      {
        foreach (KeyValuePair<string, CancellationTokenSource> pair in gates) old.Add(pair.Value);
        gates.Clear();
        if (sources != null)
        {
          foreach (SourceConfig source in sources)
          {
            if (source == null || !source.Enabled) continue;
            CancellationTokenSource cts = new CancellationTokenSource();
            gates[source.Id] = cts;
            SourceConfig copy = source;
            CancellationToken token = cts.Token;
            Task.Run(delegate
            {
              try
              {
                Supervise(copy, token, gen).Wait();
              }
              catch
              {
              }
            });
          }
        }
      }
      foreach (CancellationTokenSource cts in old)
      {
        try { cts.Cancel(); } catch { }
      }
    }

    public void StopAll()
    {
      Restart(new List<SourceConfig>());
    }

    void EmitStatus(int gen, string id, string state, string detail)
    {
      if (gen != generation) return;
      StatusHandler handler = Status;
      if (handler != null) handler(id, state, detail);
    }

    void EmitMessage(int gen, ChatMessage message)
    {
      if (gen != generation) return;
      MessageHandler handler = Message;
      if (handler != null) handler(message);
      EmoteArt.Want(message.Icons);
    }

    async Task Supervise(SourceConfig source, CancellationToken ct, int gen)
    {
      int attempt = 0;
      while (!ct.IsCancellationRequested && gen == generation)
      {
        EmitStatus(gen, source.Id, attempt == 0 ? "connecting" : "reconnecting", attempt == 0 ? "Подключаюсь" : "Поднимаю связь снова");
        bool pause = false;
        int waitMs = 0;
        try
        {
          await RunSource(source, ct, gen);
          if (ct.IsCancellationRequested || gen != generation) return;
          throw new Exception("Поток закрылся");
        }
        catch (Exception ex)
        {
          if (ct.IsCancellationRequested || gen != generation) return;
          attempt = attempt + 1;
          Exception root = ex;
          AggregateException agg = ex as AggregateException;
          if (agg != null && agg.InnerException != null) root = agg.InnerException;
          HaltException halt = root as HaltException;
          string detail = root.Message;
          if (detail == null || detail.Length == 0) detail = "Сбой связи";
          int wait = 1000 * (1 << Math.Min(attempt, 4));
          if (wait > 15000) wait = 15000;
          if (halt != null)
          {
            detail = halt.Message;
            wait = halt.RetryMs;
          }
          EmitStatus(gen, source.Id, "reconnecting", detail);
          pause = true;
          waitMs = wait;
        }
        if (pause)
        {
          try
          {
            await Task.Delay(waitMs, ct);
          }
          catch
          {
            return;
          }
        }
      }
    }

    Task RunSource(SourceConfig source, CancellationToken ct, int gen)
    {
      if (source.Platform == "twitch") return RunTwitch(source, ct, gen);
      if (source.Platform == "youtube") return RunYoutube(source, ct, gen);
      if (source.Platform == "vk") return RunVk(source, ct, gen);
      if (source.Platform == "goodgame") return RunGoodgame(source, ct, gen);
      if (source.Platform == "rutube") return RunRutube(source, ct, gen);
      if (source.Platform == "wtv") return RunWtv(source, ct);
      if (source.Platform == "steam") return RunSteam(source, ct, gen);
      if (source.Platform == "da") return RunAlerts(source, ct, gen);
      if (source.Platform == "boosty") return RunBoosty(source, ct, gen);
      throw new HaltException("Неизвестная площадка.", 60000);
    }

    async Task RunTwitch(SourceConfig source, CancellationToken ct, int gen)
    {
      string channel = source.Input.Trim().ToLowerInvariant();
      channel = Regex.Replace(channel, "^https?://(www\\.)?twitch\\.tv/", "");
      int slash = channel.IndexOf('/');
      if (slash >= 0) channel = channel.Substring(0, slash);
      if (channel.StartsWith("#")) channel = channel.Substring(1);
      if (!Regex.IsMatch(channel, "^[a-z0-9_]{2,25}$"))
      {
        throw new HaltException("Нужен ник канала Twitch, без ссылки на клип или запись.", 60000);
      }
      Task<Dictionary<string, string>> extraTask = EmoteBook.TwitchWords(channel, ct);
      Dictionary<string, string> extra = null;
      WsClient ws = new WsClient();
      try
      {
        await ws.Connect("wss://irc-ws.chat.twitch.tv:443", ct, null);
        int nick = 10000 + new Random().Next(89999);
        await ws.Send("CAP REQ :twitch.tv/tags twitch.tv/commands\r\n", ct);
        await ws.Send("PASS SCHMOOPIIE\r\n", ct);
        await ws.Send("NICK justinfan" + nick.ToString(CultureInfo.InvariantCulture) + "\r\n", ct);
        await ws.Send("JOIN #" + channel + "\r\n", ct);
        string buffer = "";
        bool joined = false;
        while (!ct.IsCancellationRequested)
        {
          CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
          idle.CancelAfter(180000);
          string raw;
          try
          {
            raw = await ws.Receive(idle.Token);
          }
          catch (OperationCanceledException)
          {
            if (ct.IsCancellationRequested) return;
            throw new Exception("Twitch замолчал, поднимаю связь");
          }
          finally
          {
            idle.Dispose();
          }
          buffer = buffer + raw;
          string[] lines = buffer.Split(new char[] { '\n' });
          buffer = lines[lines.Length - 1];
          int i;
          for (i = 0; i < lines.Length - 1; i++)
          {
            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("PING"))
            {
              await ws.Send(line.Replace("PING", "PONG") + "\r\n", ct);
              continue;
            }
            IrcLine parsed = IrcLine.Parse(line);
            if (parsed.Cmd == "JOIN" && !joined)
            {
              joined = true;
              EmitStatus(gen, source.Id, "live", "чат #" + channel);
            }
            if (parsed.Cmd == "NOTICE")
            {
              string note = parsed.Args.Count == 0 ? "" : parsed.Args[parsed.Args.Count - 1];
              if (Regex.IsMatch(note, "not available|does not exist|suspended|msg_channel_suspended|msg_banned", RegexOptions.IgnoreCase))
              {
                throw new HaltException("Такого канала Twitch нет или он закрыт.", 30000);
              }
            }
            if (parsed.Cmd != "PRIVMSG") continue;
            if (extra == null)
            {
              if (!extraTask.IsCompleted)
              {
                await Task.WhenAny(extraTask, Task.Delay(2000, ct));
              }
              try
              {
                if (extraTask.Status == TaskStatus.RanToCompletion) extra = extraTask.Result;
              }
              catch
              {
              }
              if (extra == null) extra = new Dictionary<string, string>();
            }
            string rawText = parsed.Args.Count == 0 ? "" : parsed.Args[parsed.Args.Count - 1];
            string icons;
            string planted = EmoteBook.TwitchNative(rawText, parsed.Tag("emotes"), out icons);
            planted = EmoteBook.Dress(planted, extra, null, icons, out icons);
            string text = Net.Clean(planted);
            icons = EmoteBook.Fit(text, icons);
            if (text.Length == 0) continue;
            string author = parsed.Tag("display-name");
            if (author.Length == 0)
            {
              author = parsed.Prefix;
              int bang = author.IndexOf('!');
              if (bang >= 0) author = author.Substring(0, bang);
            }
            if (author.Length == 0) author = "зритель";
            string rawId = parsed.Tag("id");
            if (rawId.Length == 0) rawId = author + ":" + parsed.Tag("tmi-sent-ts") + ":" + text.Substring(0, Math.Min(24, text.Length));
            ChatMessage message = new ChatMessage();
            message.Id = "twitch:" + source.Id + ":" + rawId;
            message.SourceId = source.Id;
            message.Platform = "twitch";
            message.Author = author;
            message.Text = text;
            message.Icons = icons;
            message.Ts = Net.ToMs(parsed.Tag("tmi-sent-ts"));
            message.Fresh = true;
            EmitMessage(gen, message);
          }
        }
      }
      finally
      {
        ws.Dispose();
      }
    }

    async Task RunYoutube(SourceConfig source, CancellationToken ct, int gen)
    {
      YtBoot boot = await BootstrapYoutube(source.Input, ct);
      EmitStatus(gen, source.Id, "live", boot.LiveId.Length > 0 ? "эфир " + boot.LiveId : "чат эфира");
      string continuation = boot.Continuation;
      bool first = true;
      while (!ct.IsCancellationRequested)
      {
        Dictionary<string, object> client = new Dictionary<string, object>();
        client["clientName"] = "WEB";
        client["clientVersion"] = boot.ClientVersion;
        Dictionary<string, object> context = new Dictionary<string, object>();
        context["client"] = client;
        Dictionary<string, object> body = new Dictionary<string, object>();
        body["context"] = context;
        body["continuation"] = continuation;
        string url = "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key=" + Uri.EscapeDataString(boot.ApiKey) + "&prettyPrint=false";
        string raw = await Net.PostJson(url, Net.Serialize(body), ct);
        Dictionary<string, object> json = Net.Parse(raw);
        Dictionary<string, object> contents = Net.Dict(json, "continuationContents");
        Dictionary<string, object> live = Net.Dict(contents, "liveChatContinuation");
        if (live == null) throw new Exception("Поток чата YouTube прервался, поднимаю заново.");
        IList actions = Net.List(live, "actions");
        if (actions != null)
        {
          foreach (object actionObj in actions)
          {
            Dictionary<string, object> action = actionObj as Dictionary<string, object>;
            Dictionary<string, object> add = Net.Dict(action, "addChatItemAction");
            Dictionary<string, object> item = Net.Dict(add, "item");
            if (item == null) continue;
            Dictionary<string, object> renderer = null;
            foreach (KeyValuePair<string, object> pair in item)
            {
              renderer = pair.Value as Dictionary<string, object>;
              if (renderer != null) break;
            }
            if (renderer == null) continue;
            string icons;
            string text = YoutubeText(renderer, out icons);
            string author = YoutubeAuthor(renderer);
            if (author.Length == 0 || text.Length == 0) continue;
            string rawId = Net.S(Net.Get(renderer, "id"));
            if (rawId.Length == 0)
            {
              rawId = author + ":" + Net.S(Net.Get(renderer, "timestampUsec")) + ":" + text.Substring(0, Math.Min(24, text.Length));
            }
            ChatMessage message = new ChatMessage();
            message.Id = "youtube:" + source.Id + ":" + rawId;
            message.SourceId = source.Id;
            message.Platform = "youtube";
            message.Author = author;
            message.Text = text;
            message.Icons = icons;
            object usec = Net.Get(renderer, "timestampUsec");
            message.Ts = usec == null ? Net.Now() : Net.ToMs(usec) / 1000L;
            if (usec != null)
            {
              double stamp;
              if (double.TryParse(Net.S(usec), NumberStyles.Float, CultureInfo.InvariantCulture, out stamp))
              {
                message.Ts = (long)(stamp / 1000d);
              }
            }
            message.Fresh = !first;
            EmitMessage(gen, message);
          }
        }
        first = false;
        IList conts = Net.List(live, "continuations");
        if (conts == null || conts.Count == 0) throw new Exception("Чат YouTube закрылся — эфир, похоже, закончился.");
        Dictionary<string, object> pack = conts[0] as Dictionary<string, object>;
        Dictionary<string, object> next = Net.Dict(pack, "invalidationContinuationData");
        if (next == null) next = Net.Dict(pack, "timedContinuationData");
        if (next == null) next = Net.Dict(pack, "reloadContinuationData");
        string nextToken = next == null ? null : Net.S(Net.Get(next, "continuation"));
        if (nextToken == null || nextToken.Length == 0) throw new Exception("Чат YouTube закрылся — эфир, похоже, закончился.");
        continuation = nextToken;
        int timeout = 2000;
        if (next != null && next.ContainsKey("timeoutMs"))
        {
          int parsed;
          if (int.TryParse(Net.S(next["timeoutMs"]), out parsed)) timeout = parsed;
        }
        if (timeout < 1000) timeout = 1000;
        if (timeout > 8000) timeout = 8000;
        await Task.Delay(timeout, ct);
      }
    }

    async Task<YtBoot> BootstrapYoutube(string input, CancellationToken ct)
    {
      string trimmed = input.Trim();
      string video = Net.Match(trimmed, "(?:v=|youtu\\.be/|/live/|/shorts/)([\\w-]{11})");
      string handle = Net.Match(trimmed, "(@[\\w.-]+)");
      string channel = Net.Match(trimmed, "channel/(UC[\\w-]+)");
      string url = "https://www.youtube.com/@" + trimmed.Replace("@", "") + "/live";
      if (video != null) url = "https://www.youtube.com/watch?v=" + video;
      else if (handle != null) url = "https://www.youtube.com/" + handle + "/live";
      else if (channel != null || Regex.IsMatch(trimmed, "^UC[\\w-]{20,}$"))
      {
        string id = channel != null ? channel : trimmed;
        url = "https://www.youtube.com/channel/" + id + "/live";
      }
      string html = await Net.GetText(url, ct);
      int at = html.IndexOf("\"conversationBar\":{\"liveChatRenderer\"");
      string slice = at < 0 ? html : html.Substring(at, Math.Min(2500, html.Length - at));
      string continuation = Net.Match(slice, "\"reloadContinuationData\":\\{\"continuation\":\"([^\"]+)\"");
      if (continuation == null) continuation = Net.Match(html, "\"reloadContinuationData\":\\{\"continuation\":\"([^\"]+)\"");
      string apiKey = Net.Match(html, "\"INNERTUBE_API_KEY\":\"([^\"]+)\"");
      string clientVersion = Net.Match(html, "\"clientVersion\":\"([\\d.]+)\"");
      if (continuation == null || apiKey == null || clientVersion == null)
      {
        throw new HaltException("Эфир YouTube не найден или чат закрыт. Проверьте @канал или ссылку на текущий стрим.", 20000);
      }
      string liveId = Net.Match(html, "rel=\"canonical\" href=\"https://www\\.youtube\\.com/watch\\?v=([\\w-]{11})\"");
      if (liveId == null) liveId = video == null ? "" : video;
      YtBoot boot = new YtBoot();
      boot.ApiKey = apiKey;
      boot.ClientVersion = clientVersion;
      boot.Continuation = continuation;
      boot.LiveId = liveId;
      return boot;
    }

    static string YoutubeAuthor(Dictionary<string, object> renderer)
    {
      Dictionary<string, object> authorName = Net.Dict(renderer, "authorName");
      if (authorName == null) return "";
      string author = Net.S(Net.Get(authorName, "simpleText"));
      if (author.Length > 0) return author;
      IList runs = Net.List(authorName, "runs");
      if (runs == null) return "";
      StringBuilder sb = new StringBuilder();
      foreach (object runObj in runs)
      {
        Dictionary<string, object> run = runObj as Dictionary<string, object>;
        if (run != null) sb.Append(Net.S(Net.Get(run, "text")));
      }
      return sb.ToString();
    }

    static string YoutubeText(Dictionary<string, object> renderer, out string icons)
    {
      List<string> urls = new List<string>();
      Dictionary<string, object> message = Net.Dict(renderer, "message");
      StringBuilder body = new StringBuilder();
      IList runs = message == null ? null : Net.List(message, "runs");
      if (runs != null)
      {
        foreach (object runObj in runs)
        {
          Dictionary<string, object> run = runObj as Dictionary<string, object>;
          if (run == null) continue;
          string text = Net.S(Net.Get(run, "text"));
          Dictionary<string, object> emoji = Net.Dict(run, "emoji");
          string url = YoutubeThumb(emoji);
          if (url.Length > 0)
          {
            body.Append('\uFFFC');
            urls.Add(url);
          }
          else if (text.Length > 0) body.Append(text);
        }
      }
      Dictionary<string, object> paid = Net.Dict(renderer, "purchaseAmountText");
      string paidText = paid == null ? "" : Net.S(Net.Get(paid, "simpleText"));
      string combined = paidText.Length > 0 ? paidText + " " + body.ToString() : body.ToString();
      icons = EmoteBook.Join(urls);
      string cleaned = Net.Clean(combined);
      icons = EmoteBook.Fit(cleaned, icons);
      return cleaned;
    }

    static string YoutubeThumb(Dictionary<string, object> emoji)
    {
      if (emoji == null) return "";
      Dictionary<string, object> image = Net.Dict(emoji, "image");
      IList thumbs = image == null ? null : Net.List(image, "thumbnails");
      string best = "";
      int bestWidth = 0;
      if (thumbs != null)
      {
        foreach (object item in thumbs)
        {
          Dictionary<string, object> thumb = item as Dictionary<string, object>;
          if (thumb == null) continue;
          string url = Net.S(Net.Get(thumb, "url"));
          if (url.Length == 0) continue;
          int width;
          int.TryParse(Net.S(Net.Get(thumb, "width")), out width);
          if (best.Length == 0 || (width >= 24 && (bestWidth < 24 || width < bestWidth)))
          {
            best = url;
            bestWidth = width;
          }
        }
      }
      if (best.StartsWith("//")) best = "https:" + best;
      return best;
    }

    async Task RunVk(SourceConfig source, CancellationToken ct, int gen)
    {
      string slug = VkSlug(source.Input);
      if (!Regex.IsMatch(slug, "^[\\w.-]{2,80}$"))
      {
        throw new HaltException("Нужен ник канала VK Видео или ссылка live.vkvideo.ru/ник.", 60000);
      }
      string infoRaw = await Net.GetText("https://api.live.vkvideo.ru/v1/blog/" + Uri.EscapeDataString(slug) + "/public_video_stream/chat/user/", ct);
      Dictionary<string, object> info = Net.Parse(infoRaw);
      Dictionary<string, object> data = Net.Dict(info, "data");
      Dictionary<string, object> owner = Net.Dict(data, "owner");
      string channelId = owner == null ? "" : Net.S(Net.Get(owner, "id"));
      if (channelId.Length == 0) throw new HaltException("У этого канала VK нет публичного чата.", 45000);
      string display = owner == null ? slug : Net.S(Net.Get(owner, "displayName"));
      if (display.Length == 0) display = slug;
      string token = await VkToken(ct);
      WsClient ws = new WsClient();
      try
      {
        await ws.Connect("wss://pubsub.live.vkvideo.ru/connection/websocket?cf_protocol_version=v2", ct, "https://live.vkvideo.ru");
        int rpc = 1;
        Dictionary<string, object> connect = new Dictionary<string, object>();
        Dictionary<string, object> connectBody = new Dictionary<string, object>();
        connectBody["token"] = token;
        connectBody["name"] = "js";
        connect["connect"] = connectBody;
        connect["id"] = rpc;
        rpc = rpc + 1;
        await ws.Send(Net.Serialize(connect), ct);
        bool subscribed = false;
        while (!ct.IsCancellationRequested)
        {
          string raw = await ws.Receive(ct);
          if (raw == "{}")
          {
            await ws.Send("{}", ct);
            continue;
          }
          Dictionary<string, object> message = Net.Parse(raw);
          if (message == null) continue;
          if (message.ContainsKey("connect") && !subscribed)
          {
            Dictionary<string, object> sub = new Dictionary<string, object>();
            Dictionary<string, object> subBody = new Dictionary<string, object>();
            subBody["channel"] = "channel-chat:" + channelId;
            sub["subscribe"] = subBody;
            sub["id"] = rpc;
            rpc = rpc + 1;
            await ws.Send(Net.Serialize(sub), ct);
            continue;
          }
          if (message.ContainsKey("subscribe") && !message.ContainsKey("error"))
          {
            subscribed = true;
            EmitStatus(gen, source.Id, "live", display);
            continue;
          }
          if (message.ContainsKey("error")) throw new HaltException("VK не пустил в чат канала.", 20000);
          Dictionary<string, object> push = Net.Dict(message, "push");
          Dictionary<string, object> pub = Net.Dict(push, "pub");
          Dictionary<string, object> payload = Net.Dict(pub, "data");
          if (payload == null || Net.S(Net.Get(payload, "type")) != "message") continue;
          Dictionary<string, object> inner = Net.Dict(payload, "data");
          Dictionary<string, object> authorRecord = Net.Dict(inner, "author");
          string author = authorRecord == null ? "" : Net.S(Net.Get(authorRecord, "displayName"));
          string icons;
          string text = VkText(inner == null ? null : Net.Get(inner, "data"), out icons);
          if (author.Length == 0 || text.Length == 0) continue;
          string rawId = inner == null ? "" : Net.S(Net.Get(inner, "id"));
          if (rawId.Length == 0) rawId = author + ":" + text.Substring(0, Math.Min(24, text.Length));
          ChatMessage chat = new ChatMessage();
          chat.Id = "vk:" + source.Id + ":" + rawId;
          chat.SourceId = source.Id;
          chat.Platform = "vk";
          chat.Author = author;
          chat.Text = text;
          chat.Icons = icons;
          chat.Ts = Net.ToMs(inner == null ? null : Net.Get(inner, "createdAt"));
          chat.Fresh = true;
          EmitMessage(gen, chat);
        }
      }
      finally
      {
        ws.Dispose();
      }
    }

    static string VkSlug(string input)
    {
      string match = Net.Match(input.Trim(), "(?:live\\.)?vkvideo\\.ru/([^/?#]+)");
      string slug = match != null ? Uri.UnescapeDataString(match) : input.Trim();
      if (slug.StartsWith("@")) slug = slug.Substring(1);
      return slug;
    }

    static string VkText(object partsObj, out string icons)
    {
      icons = "";
      IList parts = partsObj as IList;
      if (parts == null) return "";
      StringBuilder text = new StringBuilder();
      List<string> urls = new List<string>();
      foreach (object partObj in parts)
      {
        Dictionary<string, object> record = partObj as Dictionary<string, object>;
        if (record == null) continue;
        string type = Net.S(Net.Get(record, "type")).ToLowerInvariant();
        if (type == "smile" || type == "sticker" || type.IndexOf("emoji") >= 0)
        {
          string url = SmileUrl(record);
          string name = Net.S(Net.Get(record, "name"));
          if (name.Length == 0) name = Net.S(Net.Get(record, "code"));
          if (url.Length > 0)
          {
            if (name.Length > 0)
            {
              string token = ":" + name + ":";
              if (text.ToString().IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
              {
                string swapped = Regex.Replace(text.ToString(), Regex.Escape(token), "\uFFFC", RegexOptions.IgnoreCase);
                text.Length = 0;
                text.Append(swapped);
                urls.Add(url);
                continue;
              }
            }
            text.Append('\uFFFC');
            urls.Add(url);
          }
          continue;
        }
        if (record.ContainsKey("content") && (type == "text" || type.Length == 0))
        {
          string content = Net.S(record["content"]);
          try
          {
            object parsed = Net.Box(content);
            IList arr = parsed as IList;
            if (arr != null && arr.Count > 0 && arr[0] is string) text.Append((string)arr[0]);
            else text.Append(content);
          }
          catch
          {
            text.Append(content);
          }
        }
        else if (record.ContainsKey("text")) text.Append(Net.S(record["text"]));
      }
      string combined = Net.Clean(text.ToString());
      icons = EmoteBook.Fit(combined, EmoteBook.Join(urls));
      return combined;
    }

    static string SmileUrl(Dictionary<string, object> record)
    {
      if (record == null) return "";
      string[] keys = new string[] { "smallUrl", "mediumUrl", "largeUrl", "url", "imageUrl", "src", "image" };
      int i;
      for (i = 0; i < keys.Length; i++)
      {
        string value = Net.S(Net.Get(record, keys[i]));
        if (value.StartsWith("//")) value = "https:" + value;
        if (value.StartsWith("http://") || value.StartsWith("https://")) return value;
      }
      foreach (object value in record.Values)
      {
        Dictionary<string, object> nested = value as Dictionary<string, object>;
        if (nested == null) continue;
        string found = SmileUrl(nested);
        if (found.Length > 0) return found;
      }
      return "";
    }

    async Task<string> VkToken(CancellationToken ct)
    {
      long now = Net.Now() / 1000L;
      if (vkToken != null && vkTokenExp - 120 > now) return vkToken;
      string html = await Net.GetText("https://live.vkvideo.ru/", ct);
      Match found = Regex.Match(html, "<script type=\"text/plain\" id=\"app-config\">([\\s\\S]*?)</script>");
      if (!found.Success) throw new Exception("VK Видео не отдал конфиг чата.");
      Dictionary<string, object> config = Net.Parse(found.Groups[1].Value);
      Dictionary<string, object> websocket = Net.Dict(config, "websocket");
      string token = websocket == null ? "" : Net.S(Net.Get(websocket, "token"));
      if (token.Length == 0) throw new Exception("VK Видео не отдал токен чата.");
      string[] parts = token.Split('.');
      long exp = now + 3600;
      if (parts.Length > 1)
      {
        try
        {
          string payload = parts[1].Replace('-', '+').Replace('_', '/');
          int pad = payload.Length % 4;
          if (pad == 2) payload = payload + "==";
          else if (pad == 3) payload = payload + "=";
          string json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
          Dictionary<string, object> body = Net.Parse(json);
          long parsed;
          if (body != null && long.TryParse(Net.S(Net.Get(body, "exp")), out parsed)) exp = parsed;
        }
        catch
        {
        }
      }
      vkToken = token;
      vkTokenExp = exp;
      return token;
    }

    async Task RunGoodgame(SourceConfig source, CancellationToken ct, int gen)
    {
      string trimmed = source.Input.Trim();
      trimmed = Regex.Replace(trimmed, "^https?://(www\\.)?goodgame\\.ru/", "", RegexOptions.IgnoreCase);
      int slash = trimmed.IndexOf('/');
      if (slash >= 0) trimmed = trimmed.Substring(0, slash);
      string channelId = trimmed;
      if (!Regex.IsMatch(trimmed, "^\\d+$"))
      {
        if (!Regex.IsMatch(trimmed, "^[\\w.-]{2,40}$")) throw new HaltException("Нужен ник канала GoodGame.", 60000);
        string raw = await Net.GetText("https://goodgame.ru/api/4/users/" + Uri.EscapeDataString(trimmed) + "/stream", ct);
        Dictionary<string, object> data = Net.Parse(raw);
        channelId = data == null ? "" : Net.S(Net.Get(data, "id"));
        if (channelId.Length == 0) throw new HaltException("Канал GoodGame не найден.", 45000);
      }
      Task<Dictionary<string, string>> smileTask = EmoteBook.GoodGame(channelId, ct);
      Dictionary<string, string> smiles = null;
      WsClient ws = new WsClient();
      try
      {
        await ws.Connect("wss://chat-1.goodgame.ru/chat2/", ct, null);
        Dictionary<string, object> join = new Dictionary<string, object>();
        Dictionary<string, object> joinData = new Dictionary<string, object>();
        joinData["channel_id"] = channelId;
        joinData["hidden"] = 0;
        joinData["mobile"] = false;
        joinData["reload"] = false;
        join["type"] = "join";
        join["data"] = joinData;
        await ws.Send(Net.Serialize(join), ct);
        Task ping = PingGoodgame(ws, ct);
        try
        {
          while (!ct.IsCancellationRequested)
          {
            string raw = await ws.Receive(ct);
            Dictionary<string, object> message = Net.Parse(raw);
            if (message == null) continue;
            string type = Net.S(Net.Get(message, "type"));
            Dictionary<string, object> data = Net.Dict(message, "data");
            if (type == "success_join")
            {
              string name = data == null ? "" : Net.S(Net.Get(data, "channel_name"));
              if (name.Length == 0) name = "чат канала";
              EmitStatus(gen, source.Id, "live", name);
            }
            if (type == "error") throw new HaltException("GoodGame отклонил канал.", 30000);
            if (type != "message" || data == null) continue;
            if (smiles == null)
            {
              if (!smileTask.IsCompleted) await Task.WhenAny(smileTask, Task.Delay(2000, ct));
              try
              {
                if (smileTask.Status == TaskStatus.RanToCompletion) smiles = smileTask.Result;
              }
              catch
              {
              }
              if (smiles == null) smiles = new Dictionary<string, string>();
            }
            string icons;
            string planted = EmoteBook.Dress(Net.S(Net.Get(data, "text")), null, smiles, "", out icons);
            string text = Net.Clean(planted);
            icons = EmoteBook.Fit(text, icons);
            string author = Net.S(Net.Get(data, "user_name"));
            if (author.Length == 0) author = "зритель";
            if (text.Length == 0) continue;
            string rawId = Net.S(Net.Get(data, "message_id"));
            if (rawId.Length == 0) rawId = author + ":" + text.Substring(0, Math.Min(24, text.Length));
            ChatMessage chat = new ChatMessage();
            chat.Id = "gg:" + source.Id + ":" + rawId;
            chat.SourceId = source.Id;
            chat.Platform = "goodgame";
            chat.Author = author;
            chat.Text = text;
            chat.Icons = icons;
            chat.Ts = Net.ToMs(Net.Get(data, "timestamp"));
            chat.Fresh = true;
            EmitMessage(gen, chat);
          }
        }
        catch (Exception)
        {
          try { ping.Wait(1500); } catch { }
          throw;
        }
        try { ping.Wait(1500); } catch { }
      }
      finally
      {
        ws.Dispose();
      }
    }

    static async Task PingGoodgame(WsClient ws, CancellationToken ct)
    {
      while (!ct.IsCancellationRequested)
      {
        try
        {
          await Task.Delay(25000, ct);
        }
        catch
        {
          return;
        }
        try
        {
          await ws.Send("{\"type\":\"ping\",\"data\":{}}", ct);
        }
        catch
        {
          return;
        }
      }
    }

    async Task RunRutube(SourceConfig source, CancellationToken ct, int gen)
    {
      HashSet<string> seen = new HashSet<string>();
      while (!ct.IsCancellationRequested)
      {
        string videoId = await FindRutubeLive(source.Input, ct);
        if (videoId == null)
        {
          EmitStatus(gen, source.Id, "reconnecting", "На канале сейчас нет эфира. Жду видео с пометкой Онлайн.");
          await Task.Delay(15000, ct);
          continue;
        }
        EmitStatus(gen, source.Id, "live", "Эфир найден, читаю чат");
        bool first = true;
        int misses = 0;
        while (!ct.IsCancellationRequested && misses < 3)
        {
          string raw;
          try
          {
            raw = await Net.GetText("https://rutube.ru/api/chat/" + videoId + "/?client=wdp&limit=30", ct);
            misses = 0;
          }
          catch
          {
            misses = misses + 1;
            await Task.Delay(4000, ct);
            continue;
          }
          Dictionary<string, object> json = Net.Parse(raw);
          IList results = Net.List(json, "results");
          if (results != null)
          {
            List<Dictionary<string, object>> ordered = new List<Dictionary<string, object>>();
            foreach (object item in results)
            {
              Dictionary<string, object> row = item as Dictionary<string, object>;
              if (row != null) ordered.Add(row);
            }
            ordered.Reverse();
            foreach (Dictionary<string, object> item in ordered)
            {
              Dictionary<string, object> payload = Net.Dict(item, "payload");
              if (payload == null) payload = item;
              string rawId = Net.S(Net.Get(payload, "id"));
              if (rawId.Length == 0 || seen.Contains(rawId)) continue;
              seen.Add(rawId);
              string icons;
              string text = EmoteBook.FromPayload(payload, Net.Clean(Net.S(Net.Get(payload, "text"))), out icons);
              string donate = Net.S(Net.Get(payload, "donate_amount"));
              if (donate.Length > 0 && donate != "0") text = (text + " донат " + donate).Trim();
              if (text.Length == 0) continue;
              Dictionary<string, object> user = Net.Dict(payload, "user");
              string author = user == null ? "" : Net.S(Net.Get(user, "name"));
              if (author.Length == 0) author = "зритель";
              ChatMessage chat = new ChatMessage();
              chat.Id = "rutube:" + source.Id + ":" + rawId;
              chat.SourceId = source.Id;
              chat.Platform = "rutube";
              chat.Author = author;
              chat.Text = text;
              chat.Icons = icons;
              chat.Ts = Net.ToMs(Net.Get(payload, "created_ts"));
              chat.Fresh = !first;
              EmitMessage(gen, chat);
            }
          }
          first = false;
          await Task.Delay(4000, ct);
        }
      }
    }

    public static async Task<string> FindRutubeLive(string input, CancellationToken ct)
    {
      string trimmed = input == null ? "" : input.Trim();
      string video = Net.Match(trimmed, "/(?:live/)?video/(?:private/)?([a-f0-9]{32})");
      if (video != null) return video;
      string channel = Net.Match(trimmed, "/(?:channel|person)/(\\d+)");
      if (channel == null && Regex.IsMatch(trimmed, "^\\d{4,12}$")) channel = trimmed;
      if (channel == null) throw new HaltException("Нужна ссылка на канал Rutube или на сам эфир.", 60000);
      string raw = await Net.GetText("https://rutube.ru/api/video/person/" + channel + "/?page=1&limit=20", ct);
      Dictionary<string, object> json = Net.Parse(raw);
      IList results = Net.List(json, "results");
      if (results == null) return null;
      foreach (object item in results)
      {
        Dictionary<string, object> row = item as Dictionary<string, object>;
        if (row == null) continue;
        if (!IsTrue(Net.Get(row, "is_on_air"))) continue;
        string id = Net.S(Net.Get(row, "id"));
        if (Regex.IsMatch(id, "^[a-f0-9]{32}$")) return id;
      }
      return null;
    }

    static bool IsTrue(object value)
    {
      if (value is bool) return (bool)value;
      string text = Net.S(value).ToLowerInvariant();
      return text == "true" || text == "1";
    }

    async Task RunAlerts(SourceConfig source, CancellationToken ct, int gen)
    {
      string token = AlertToken(source.Input);
      if (token.Length < 8) throw new HaltException("Нужна целиком ссылка виджета DonationAlerts из адресной строки.", 60000);
      string preferred = await AlertSocket(token, ct);
      List<string> urls = new List<string>();
      urls.Add(preferred);
      urls.Add("wss://socket11.donationalerts.com");
      urls.Add("wss://socket.donationalerts.com");
      int n;
      for (n = 1; n <= 12; n++) urls.Add("wss://socket" + n.ToString(CultureInfo.InvariantCulture) + ".donationalerts.com");
      Exception last = null;
      int i;
      for (i = 0; i < urls.Count; i++)
      {
        string url = urls[i] + "/socket.io/?EIO=3&transport=websocket";
        try
        {
          await AlertsOnce(source, token, url, ct, gen);
          return;
        }
        catch (Exception ex)
        {
          if (ct.IsCancellationRequested) throw;
          HaltException halt = ex as HaltException;
          if (halt != null) throw;
          last = ex;
        }
      }
      throw new HaltException(last == null ? "DonationAlerts не открылся." : "DonationAlerts не ответил. Вставь полную ссылку виджета.", 20000);
    }

    static async Task<string> AlertSocket(string token, CancellationToken ct)
    {
      string pageUrl = "https://www.donationalerts.com/widget/alerts?group_id=1&token=" + Uri.EscapeDataString(token);
      try
      {
        string page = await Net.GetText(pageUrl, ct);
        string host = Net.Match(page, "(wss://socket\\d*\\.donationalerts\\.com)");
        if (host != null && host.Length > 0) return host;
      }
      catch (Exception ex)
      {
        string message = ex.Message == null ? "" : ex.Message;
        if (message.IndexOf("422", StringComparison.Ordinal) >= 0)
        {
          throw new HaltException("Этот токен DonationAlerts не узнаёт. Скопируй ссылку виджета целиком из адресной строки браузера.", 60000);
        }
      }
      return "wss://socket11.donationalerts.com";
    }

    async Task AlertsOnce(SourceConfig source, string token, string url, CancellationToken ct, int gen)
    {
      using (WsClient ws = new WsClient())
      {
        Task connecting = ws.Connect(url, ct, "https://www.donationalerts.com");
        Task limit = Task.Delay(8000, ct);
        if (await Task.WhenAny(connecting, limit) != connecting)
        {
          ws.Abort();
          throw new Exception("нет ответа");
        }
        await connecting;
        string open = await ws.Receive(ct);
        if (open == null || open.Length == 0 || open[0] != '0') throw new Exception("протокол");
        await ws.Send("2probe", ct);
        string probe = await ws.Receive(ct);
        if (probe != null && probe.IndexOf("3probe", StringComparison.Ordinal) >= 0)
        {
          await ws.Send("5", ct);
        }
        await ws.Send("40", ct);
        Dictionary<string, object> payload = new Dictionary<string, object>();
        payload["token"] = token;
        payload["type"] = "minor";
        object[] call = new object[] { "add-user", payload };
        await ws.Send("42" + Net.Serialize(call), ct);
        EmitStatus(gen, source.Id, "live", "Слушаю донаты");
        while (!ct.IsCancellationRequested)
        {
          string raw = await ws.Receive(ct);
          if (raw == null) continue;
          if (raw == "2")
          {
            await ws.Send("3", ct);
            continue;
          }
          if (!raw.StartsWith("42")) continue;
          JArray arr;
          try { arr = JArray.Parse(raw.Substring(2)); }
          catch { continue; }
          if (arr.Count < 2) continue;
          string eventName = arr[0].ToString();
          if (!string.Equals(eventName, "donation", StringComparison.OrdinalIgnoreCase)) continue;
          JToken data = arr[1];
          if (data.Type == JTokenType.String)
          {
            try { data = JToken.Parse(data.ToString()); }
            catch { continue; }
          }
          string author = FirstText(data, "username", "name");
          if (author.Length == 0) author = "Донат";
          string amount = FirstText(data, "amount_formatted", "amount", "amount_main");
          string currency = FirstText(data, "currency");
          string text = FirstText(data, "message", "comment");
          string spoken = amount;
          if (currency.Length > 0 && spoken.IndexOf(currency, StringComparison.OrdinalIgnoreCase) < 0) spoken = (spoken + " " + currency).Trim();
          if (text.Length > 0) spoken = (spoken + ". " + text).Trim();
          if (spoken.Length == 0) spoken = "новый донат";
          string rawId = FirstText(data, "id", "_id");
          if (rawId.Length == 0) rawId = author + ":" + amount + ":" + text;
          ChatMessage chat = new ChatMessage();
          chat.Id = "da:" + source.Id + ":" + rawId;
          chat.SourceId = source.Id;
          chat.Platform = "da";
          chat.Author = author;
          chat.Text = spoken;
          chat.Ts = Net.Now();
          chat.Fresh = true;
          EmitMessage(gen, chat);
        }
      }
    }

    static string AlertToken(string input)
    {
      if (input == null) return "";
      string raw = input.Trim();
      string fromUrl = Net.Match(raw, "[?&]token=([^&\\s]+)");
      if (fromUrl != null && fromUrl.Length > 0) return Uri.UnescapeDataString(fromUrl);
      return raw;
    }

    async Task RunBoosty(SourceConfig source, CancellationToken ct, int gen)
    {
      string name = source.Input.Trim();
      name = Regex.Replace(name, "^https?://(?:www\\.)?boosty\\.to/", "", RegexOptions.IgnoreCase);
      int slash = name.IndexOf('/');
      if (slash >= 0) name = name.Substring(0, slash);
      name = name.Trim().Trim('/');
      if (!Regex.IsMatch(name, "^[\\w.-]{2,40}$")) throw new HaltException("Нужен ник блога, например ghostpot.", 60000);
      while (!ct.IsCancellationRequested)
      {
        string blogRaw = await Net.GetText("https://api.boosty.to/v1/blog/" + Uri.EscapeDataString(name), ct);
        Dictionary<string, object> blog = Net.Parse(blogRaw);
        string channel = blog == null ? "" : Net.S(Net.Get(blog, "publicWebSocketChannel"));
        if (channel.Length == 0) throw new HaltException("Boosty не отдал канал блога.", 30000);
        string page = await Net.GetText("https://boosty.to/" + Uri.EscapeDataString(name), ct);
        string token = Net.Match(page, "\"token\"\\s*:\\s*\"(eyJ[A-Za-z0-9_\\-\\.]+)\"");
        if (token == null || token.Length < 20) throw new HaltException("Boosty не отдал ключ ленты. Открой блог в браузере и проверь ник.", 30000);
        using (WsClient ws = new WsClient())
        {
          await ws.Connect("wss://pubsub.boosty.to/connection/websocket?format=json", ct, "https://boosty.to");
          Dictionary<string, object> connectToken = new Dictionary<string, object>();
          connectToken["token"] = token;
          Dictionary<string, object> connect = new Dictionary<string, object>();
          connect["id"] = 1;
          connect["connect"] = connectToken;
          await ws.Send(Net.Serialize(connect), ct);
          string hello = await ws.Receive(ct);
          if (hello == null || hello.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 && hello.IndexOf("connect") < 0)
          {
            throw new HaltException("Boosty не пустил к ленте.", 20000);
          }
          Dictionary<string, object> subBody = new Dictionary<string, object>();
          subBody["channel"] = channel;
          Dictionary<string, object> sub = new Dictionary<string, object>();
          sub["id"] = 2;
          sub["subscribe"] = subBody;
          await ws.Send(Net.Serialize(sub), ct);
          EmitStatus(gen, source.Id, "live", "Слушаю донаты " + name);
          long readyAt = Net.Now() + 4000;
          while (!ct.IsCancellationRequested)
          {
            string raw = await ws.Receive(ct);
            if (raw == null || raw == "{}" || raw.Length < 3)
            {
              if (raw == "{}") await ws.Send("{}", ct);
              continue;
            }
            Dictionary<string, object> frame = Net.Parse(raw);
            if (frame == null) continue;
            Dictionary<string, object> push = Net.Dict(frame, "push");
            if (push == null) continue;
            Dictionary<string, object> pub = Net.Dict(push, "pub");
            object data = pub == null ? null : Net.Get(pub, "data");
            if (data == null) continue;
            string blob = Net.Serialize(data).ToLowerInvariant();
            if (blob.IndexOf("donat") < 0 && blob.IndexOf("tip") < 0 && blob.IndexOf("payment") < 0) continue;
            JToken node = data as JToken;
            if (node == null) node = JToken.FromObject(data);
            string author = FirstText(node, "username", "name", "userName");
            if (author.Length == 0) author = "Донат";
            string amount = FirstText(node, "amount", "sum", "money");
            string text = FirstText(node, "message", "comment", "text");
            string spoken = amount;
            if (text.Length > 0) spoken = (spoken + ". " + text).Trim();
            if (spoken.Length == 0) continue;
            if (Net.Now() < readyAt) continue;
            string rawId = FirstText(node, "id");
            if (rawId.Length == 0) rawId = author + ":" + spoken;
            ChatMessage chat = new ChatMessage();
            chat.Id = "by:" + source.Id + ":" + rawId;
            chat.SourceId = source.Id;
            chat.Platform = "boosty";
            chat.Author = author;
            chat.Text = spoken;
            chat.Ts = Net.Now();
            chat.Fresh = true;
            EmitMessage(gen, chat);
          }
        }
      }
    }

    static string FirstText(JToken node, params string[] keys)
    {
      if (node == null) return "";
      int i;
      for (i = 0; i < keys.Length; i++)
      {
        JToken found = FindToken(node, keys[i], 0);
        if (found == null || found.Type == JTokenType.Null) continue;
        string text = found.Type == JTokenType.String ? found.ToString() : found.ToString();
        text = text.Trim();
        if (text.Length > 0) return Net.Clean(text);
      }
      return "";
    }

    static JToken FindToken(JToken node, string key, int depth)
    {
      if (node == null || depth > 5) return null;
      JObject obj = node as JObject;
      if (obj != null)
      {
        JToken direct = obj[key];
        if (direct != null && direct.Type != JTokenType.Null && direct.Type != JTokenType.Object) return direct;
        foreach (JProperty prop in obj.Properties())
        {
          JToken nested = FindToken(prop.Value, key, depth + 1);
          if (nested != null) return nested;
        }
      }
      JArray arr = node as JArray;
      if (arr != null)
      {
        foreach (JToken item in arr)
        {
          JToken nested = FindToken(item, key, depth + 1);
          if (nested != null) return nested;
        }
      }
      return null;
    }

    static async Task RunWtv(SourceConfig source, CancellationToken ct)
    {
      string name = source.Input.Trim();
      name = Regex.Replace(name, "^https?://(?:www\\.)?w\\.tv/", "", RegexOptions.IgnoreCase);
      int slash = name.IndexOf('/');
      if (slash >= 0) name = name.Substring(0, slash);
      if (name.StartsWith("@")) name = name.Substring(1);
      if (!Regex.IsMatch(name, "^[\\w.-]{2,40}$")) throw new HaltException("Нужен ник канала w.tv.", 60000);
      string text = await Net.GetText("https://w.tv/" + Uri.EscapeDataString(name) + "/chat", ct);
      if (text.IndexOf("awsWaf", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("challenge.js", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        throw new HaltException("w.tv пускает чат только после проверки браузера. Лента не будет притворяться, что сообщений просто нет.", 60000);
      }
      throw new HaltException("w.tv не отдал ленту сообщений.", 30000);
    }

    async Task RunSteam(SourceConfig source, CancellationToken ct, int gen)
    {
      string steamId = await ResolveSteam(source.Input, ct);
      while (!ct.IsCancellationRequested)
      {
        string raw = await Net.GetText("https://steamcommunity.com/broadcast/getbroadcastinfo/?steamid=" + steamId, ct);
        Dictionary<string, object> info = Net.Parse(raw);
        string success = info == null ? "" : Net.S(Net.Get(info, "success"));
        if (success != "1")
        {
          EmitStatus(gen, source.Id, "reconnecting", "Трансляция Steam сейчас не идёт.");
          await Task.Delay(20000, ct);
          continue;
        }
        EmitStatus(gen, source.Id, "live", "Эфир есть, но гостевой чат Steam обычно молчит без клиента Steam.");
        await Task.Delay(20000, ct);
      }
    }

    static async Task<string> ResolveSteam(string input, CancellationToken ct)
    {
      string raw = input.Trim();
      string embedded = Net.Match(raw, "(7656\\d{13})");
      if (embedded != null) return embedded;
      string profile = Net.Match(raw, "steamcommunity\\.com/profiles/(\\d{17})");
      if (profile != null) return profile;
      string vanity = Net.Match(raw, "steamcommunity\\.com/id/([^/?#]+)");
      if (vanity == null) vanity = raw.StartsWith("@") ? raw.Substring(1) : raw;
      if (!Regex.IsMatch(vanity, "^[\\w.-]{2,64}$")) throw new HaltException("Нужна ссылка на профиль Steam или SteamID.", 60000);
      string xml = await Net.GetText("https://steamcommunity.com/id/" + Uri.EscapeDataString(vanity) + "?xml=1", ct);
      string found = Net.Match(xml, "<steamID64>(\\d{17})</steamID64>");
      if (found == null) throw new HaltException("Профиль Steam не найден.", 60000);
      return found;
    }
  }

  public class YtBoot
  {
    public string ApiKey;
    public string ClientVersion;
    public string Continuation;
    public string LiveId;
  }

  public class IrcLine
  {
    public Dictionary<string, string> Tags = new Dictionary<string, string>();
    public string Prefix = "";
    public string Cmd = "";
    public List<string> Args = new List<string>();

    public string Tag(string key)
    {
      string value;
      if (Tags.TryGetValue(key, out value) && value != null) return value;
      return "";
    }

    public static IrcLine Parse(string line)
    {
      IrcLine parsed = new IrcLine();
      string rest = line;
      if (rest.StartsWith("@"))
      {
        int space = rest.IndexOf(' ');
        string raw = space < 0 ? rest.Substring(1) : rest.Substring(1, space - 1);
        rest = space < 0 ? "" : rest.Substring(space + 1);
        string[] parts = raw.Split(';');
        foreach (string part in parts)
        {
          int eq = part.IndexOf('=');
          if (eq < 0) parsed.Tags[part] = "";
          else
          {
            string val = part.Substring(eq + 1).Replace("\\s", " ").Replace("\\:", ";").Replace("\\\\", "\\");
            parsed.Tags[part.Substring(0, eq)] = val;
          }
        }
      }
      if (rest.StartsWith(":"))
      {
        int space = rest.IndexOf(' ');
        parsed.Prefix = space < 0 ? rest.Substring(1) : rest.Substring(1, space - 1);
        rest = space < 0 ? "" : rest.Substring(space + 1);
      }
      int bang = rest.IndexOf(" :");
      string trailing = null;
      if (bang >= 0)
      {
        trailing = rest.Substring(bang + 2);
        rest = rest.Substring(0, bang);
      }
      string[] args = rest.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
      if (args.Length > 0) parsed.Cmd = args[0];
      int i;
      for (i = 1; i < args.Length; i++) parsed.Args.Add(args[i]);
      if (trailing != null) parsed.Args.Add(trailing);
      return parsed;
    }
  }

  static class EmoteArt
  {
    static readonly object gate = new object();
    static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
    static readonly HashSet<string> wait = new HashSet<string>();
    static readonly HashSet<string> bad = new HashSet<string>();
    public static int Stamp;
    public static event Action Changed;

    public static void Want(string icons)
    {
      if (icons == null || icons.Length == 0) return;
      string[] parts = icons.Split('\n');
      int i;
      for (i = 0; i < parts.Length; i++) Peek(parts[i]);
    }

    public static bool Failed(string url)
    {
      if (url == null || url.Length == 0) return true;
      lock (gate)
      {
        return bad.Contains(url);
      }
    }

    public static Image Peek(string url)
    {
      if (url == null || url.Length < 8) return null;
      if (!url.StartsWith("http://") && !url.StartsWith("https://")) return null;
      lock (gate)
      {
        Image found;
        if (cache.TryGetValue(url, out found)) return found;
        if (bad.Contains(url) || wait.Contains(url)) return null;
        if (wait.Count > 12) return null;
        wait.Add(url);
      }
      string copy = url;
      Thread thread = new Thread(new ThreadStart(delegate { Load(copy); }));
      thread.IsBackground = true;
      thread.Name = "RechEmote";
      thread.Start();
      return null;
    }

    static void Load(string url)
    {
      Image image = null;
      try
      {
        byte[] bytes = Net.Http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        if (bytes != null && bytes.Length > 32 && bytes.Length < 2000000)
        {
          using (MemoryStream ms = new MemoryStream(bytes))
          using (Image raw = Image.FromStream(ms))
          {
            Bitmap bmp = new Bitmap(Math.Max(1, raw.Width), Math.Max(1, raw.Height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
              g.DrawImage(raw, 0, 0, bmp.Width, bmp.Height);
            }
            image = bmp;
          }
        }
      }
      catch
      {
      }
      lock (gate)
      {
        wait.Remove(url);
        if (image == null) bad.Add(url);
        else
        {
          cache[url] = image;
          if (cache.Count > 500)
          {
            cache.Clear();
            cache[url] = image;
          }
          Stamp = Stamp + 1;
        }
      }
      if (image != null && Changed != null) Changed();
    }

    public static string Pict(Image image)
    {
      if (image == null) return "";
      using (MemoryStream ms = new MemoryStream())
      {
        image.Save(ms, ImageFormat.Png);
        byte[] data = ms.ToArray();
        StringBuilder hex = new StringBuilder(data.Length * 2);
        int i;
        for (i = 0; i < data.Length; i++) hex.Append(data[i].ToString("X2"));
        int goal = 20 * 15;
        return "{\\pict\\pngblip\\picw" + image.Width.ToString(CultureInfo.InvariantCulture)
          + "\\pich" + image.Height.ToString(CultureInfo.InvariantCulture)
          + "\\picwgoal" + goal.ToString(CultureInfo.InvariantCulture)
          + "\\pichgoal" + goal.ToString(CultureInfo.InvariantCulture)
          + " " + hex.ToString() + "}";
      }
    }
  }

  static class EmoteBook
  {
    static readonly object gate = new object();
    static Dictionary<string, string> twitchGlobal;
    static Dictionary<string, string> ggGlobal;

    public static string Join(List<string> urls)
    {
      if (urls == null || urls.Count == 0) return "";
      return string.Join("\n", urls.ToArray());
    }

    public static string Fit(string text, string icons)
    {
      int marks = 0;
      if (text != null)
      {
        int i;
        for (i = 0; i < text.Length; i++) if (text[i] == '\uFFFC') marks = marks + 1;
      }
      if (icons == null || icons.Length == 0 || marks == 0) return marks == 0 ? "" : icons;
      string[] parts = icons.Split('\n');
      if (parts.Length <= marks) return icons;
      StringBuilder sb = new StringBuilder();
      int n = 0;
      int p;
      for (p = 0; p < parts.Length && n < marks; p++)
      {
        if (parts[p].Length == 0) continue;
        if (n > 0) sb.Append('\n');
        sb.Append(parts[p]);
        n = n + 1;
      }
      return sb.ToString();
    }

    public static string TwitchNative(string text, string tag, out string icons)
    {
      icons = "";
      if (text == null) text = "";
      if (tag == null || tag.Length == 0) return text;
      List<int> starts = new List<int>();
      List<int> ends = new List<int>();
      List<string> urls = new List<string>();
      string[] groups = tag.Split('/');
      int g;
      for (g = 0; g < groups.Length; g++)
      {
        int colon = groups[g].IndexOf(':');
        if (colon < 1) continue;
        string id = groups[g].Substring(0, colon);
        if (!Regex.IsMatch(id, "^[0-9A-Za-z_]+$")) continue;
        string url = "https://static-cdn.jtvnw.net/emoticons/v2/" + id + "/default/dark/1.0";
        string[] ranges = groups[g].Substring(colon + 1).Split(',');
        int r;
        for (r = 0; r < ranges.Length; r++)
        {
          int dash = ranges[r].IndexOf('-');
          int start;
          int end;
          if (dash < 1) continue;
          if (!int.TryParse(ranges[r].Substring(0, dash), out start)) continue;
          if (!int.TryParse(ranges[r].Substring(dash + 1), out end)) continue;
          if (start < 0 || end < start || end >= text.Length) continue;
          starts.Add(start);
          ends.Add(end);
          urls.Add(url);
        }
      }
      if (starts.Count == 0) return text;
      int a;
      for (a = 0; a < starts.Count; a++)
      {
        int b;
        for (b = a + 1; b < starts.Count; b++)
        {
          if (starts[b] < starts[a])
          {
            int swap = starts[a];
            starts[a] = starts[b];
            starts[b] = swap;
            swap = ends[a];
            ends[a] = ends[b];
            ends[b] = swap;
            string u = urls[a];
            urls[a] = urls[b];
            urls[b] = u;
          }
        }
      }
      StringBuilder sb = new StringBuilder();
      List<string> kept = new List<string>();
      int cursor = 0;
      for (a = 0; a < starts.Count; a++)
      {
        if (starts[a] < cursor) continue;
        if (starts[a] > cursor) sb.Append(text, cursor, starts[a] - cursor);
        sb.Append('\uFFFC');
        kept.Add(urls[a]);
        cursor = ends[a] + 1;
      }
      if (cursor < text.Length) sb.Append(text, cursor, text.Length - cursor);
      icons = Join(kept);
      return sb.ToString();
    }

    public static string Dress(string text, Dictionary<string, string> words, Dictionary<string, string> colons, string icons, out string packed)
    {
      if (text == null) text = "";
      List<string> have = new List<string>();
      if (icons != null && icons.Length > 0)
      {
        string[] old = icons.Split('\n');
        int n;
        for (n = 0; n < old.Length; n++) if (old[n].Length > 0) have.Add(old[n]);
      }
      int used = 0;
      List<string> next = new List<string>();
      StringBuilder sb = new StringBuilder();
      int i = 0;
      while (i < text.Length)
      {
        if (text[i] == '\uFFFC')
        {
          sb.Append('\uFFFC');
          if (used < have.Count) next.Add(have[used]);
          used = used + 1;
          i = i + 1;
          continue;
        }
        if (text[i] == ':' && colons != null && colons.Count > 0)
        {
          int end = text.IndexOf(':', i + 1);
          if (end > i + 1 && end - i < 40)
          {
            string name = text.Substring(i + 1, end - i - 1);
            string url;
            if (Regex.IsMatch(name, "^[A-Za-z0-9_]{2,}$") && (colons.TryGetValue(name, out url) || colons.TryGetValue(name.ToLowerInvariant(), out url)))
            {
              sb.Append('\uFFFC');
              next.Add(url);
              i = end + 1;
              continue;
            }
          }
        }
        if (!char.IsWhiteSpace(text[i]) && words != null && words.Count > 0)
        {
          int j = i;
          while (j < text.Length && !char.IsWhiteSpace(text[j]) && text[j] != '\uFFFC') j = j + 1;
          string token = text.Substring(i, j - i);
          string url;
          if (words.TryGetValue(token, out url))
          {
            sb.Append('\uFFFC');
            next.Add(url);
            i = j;
            continue;
          }
        }
        sb.Append(text[i]);
        i = i + 1;
      }
      packed = Join(next);
      return sb.ToString();
    }

    public static string FromPayload(Dictionary<string, object> payload, string text, out string icons)
    {
      icons = "";
      if (text == null) text = "";
      if (payload == null) return text;
      Dictionary<string, string> colons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      Dictionary<string, string> words = new Dictionary<string, string>(StringComparer.Ordinal);
      try
      {
        JToken node = JToken.FromObject(payload);
        Walk(node, colons, words, 0);
      }
      catch
      {
      }
      if (colons.Count == 0 && words.Count == 0) return text;
      return Dress(text, words, colons, "", out icons);
    }

    static void Walk(JToken node, Dictionary<string, string> colons, Dictionary<string, string> words, int depth)
    {
      if (node == null || depth > 5) return;
      JObject obj = node as JObject;
      if (obj != null)
      {
        string type = TokenText(obj["type"]) + " " + TokenText(obj["kind"]);
        type = type.ToLowerInvariant();
        bool emote = type.IndexOf("smile") >= 0 || type.IndexOf("emote") >= 0 || type.IndexOf("emoji") >= 0 || type.IndexOf("sticker") >= 0;
        if (emote)
        {
          string name = FirstToken(obj, "name", "code", "shortcode", "shortcut", "alt");
          string url = FirstHttp(obj);
          if (name.Length > 0 && url.Length > 0)
          {
            colons[name.Trim(':')] = url;
            words[name] = url;
          }
        }
        foreach (JProperty prop in obj.Properties()) Walk(prop.Value, colons, words, depth + 1);
        return;
      }
      JArray arr = node as JArray;
      if (arr == null) return;
      foreach (JToken item in arr) Walk(item, colons, words, depth + 1);
    }

    static string TokenText(JToken token)
    {
      if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Object || token.Type == JTokenType.Array) return "";
      return token.ToString();
    }

    static string FirstToken(JObject obj, params string[] keys)
    {
      int i;
      for (i = 0; i < keys.Length; i++)
      {
        string text = TokenText(obj[keys[i]]);
        if (text.Length > 0) return text;
      }
      return "";
    }

    static string FirstHttp(JObject obj)
    {
      string[] keys = new string[] { "smallUrl", "mediumUrl", "url", "image", "src", "imageUrl", "largeUrl" };
      int i;
      for (i = 0; i < keys.Length; i++)
      {
        string text = TokenText(obj[keys[i]]);
        if (text.StartsWith("//")) text = "https:" + text;
        if (text.StartsWith("http://") || text.StartsWith("https://")) return text;
      }
      foreach (JProperty prop in obj.Properties())
      {
        JObject nested = prop.Value as JObject;
        if (nested == null) continue;
        string found = FirstHttp(nested);
        if (found.Length > 0) return found;
      }
      return "";
    }

    public static async Task<Dictionary<string, string>> TwitchWords(string channel, CancellationToken ct)
    {
      Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
      try
      {
        await EnsureTwitchGlobal(ct);
        lock (gate)
        {
          if (twitchGlobal != null)
          {
            foreach (KeyValuePair<string, string> pair in twitchGlobal) map[pair.Key] = pair.Value;
          }
        }
        string id = await TwitchId(channel, ct);
        if (id.Length > 0)
        {
          await BttvUser(map, id, ct);
          await SevenUser(map, id, ct);
        }
        await FfzRoom(map, channel, ct);
      }
      catch
      {
      }
      return map;
    }

    static async Task EnsureTwitchGlobal(CancellationToken ct)
    {
      lock (gate)
      {
        if (twitchGlobal != null) return;
      }
      Dictionary<string, string> built = new Dictionary<string, string>(StringComparer.Ordinal);
      try
      {
        string raw = await Net.GetText("https://api.betterttv.net/3/cached/emotes/global", ct);
        JArray arr = JArray.Parse(raw);
        int i;
        for (i = 0; i < arr.Count; i++)
        {
          JObject item = arr[i] as JObject;
          if (item == null) continue;
          string code = TokenText(item["code"]);
          string id = TokenText(item["id"]);
          if (code.Length > 0 && id.Length > 0) built[code] = "https://cdn.betterttv.net/emote/" + id + "/1x";
        }
      }
      catch
      {
      }
      try
      {
        string raw = await Net.GetText("https://api.frankerfacez.com/v1/set/global", ct);
        TakeFfz(built, JObject.Parse(raw));
      }
      catch
      {
      }
      lock (gate)
      {
        if (twitchGlobal == null) twitchGlobal = built;
      }
    }

    static async Task<string> TwitchId(string channel, CancellationToken ct)
    {
      try
      {
        string raw = await Net.GetText("https://api.ivr.fi/v2/twitch/user?login=" + Uri.EscapeDataString(channel), ct);
        JArray arr = JArray.Parse(raw);
        if (arr.Count == 0) return "";
        JObject item = arr[0] as JObject;
        return item == null ? "" : TokenText(item["id"]);
      }
      catch
      {
        return "";
      }
    }

    static async Task BttvUser(Dictionary<string, string> map, string id, CancellationToken ct)
    {
      try
      {
        string raw = await Net.GetText("https://api.betterttv.net/3/cached/users/twitch/" + Uri.EscapeDataString(id), ct);
        JObject root = JObject.Parse(raw);
        TakeBttv(map, root["channelEmotes"] as JArray);
        TakeBttv(map, root["sharedEmotes"] as JArray);
      }
      catch
      {
      }
    }

    static void TakeBttv(Dictionary<string, string> map, JArray arr)
    {
      if (arr == null) return;
      int i;
      for (i = 0; i < arr.Count; i++)
      {
        JObject item = arr[i] as JObject;
        if (item == null) continue;
        string code = TokenText(item["code"]);
        string id = TokenText(item["id"]);
        if (code.Length > 0 && id.Length > 0) map[code] = "https://cdn.betterttv.net/emote/" + id + "/1x";
      }
    }

    static async Task FfzRoom(Dictionary<string, string> map, string channel, CancellationToken ct)
    {
      try
      {
        string raw = await Net.GetText("https://api.frankerfacez.com/v1/room/" + Uri.EscapeDataString(channel), ct);
        TakeFfz(map, JObject.Parse(raw));
      }
      catch
      {
      }
    }

    static void TakeFfz(Dictionary<string, string> map, JObject root)
    {
      if (root == null) return;
      JObject sets = root["sets"] as JObject;
      if (sets == null) return;
      foreach (JProperty prop in sets.Properties())
      {
        JObject set = prop.Value as JObject;
        if (set == null) continue;
        JArray emotes = set["emoticons"] as JArray;
        if (emotes == null) continue;
        int i;
        for (i = 0; i < emotes.Count; i++)
        {
          JObject item = emotes[i] as JObject;
          if (item == null) continue;
          string name = TokenText(item["name"]);
          JObject urls = item["urls"] as JObject;
          if (name.Length == 0 || urls == null) continue;
          string url = TokenText(urls["1"]);
          if (url.Length == 0) url = TokenText(urls["2"]);
          if (url.StartsWith("//")) url = "https:" + url;
          if (url.StartsWith("http")) map[name] = url;
        }
      }
    }

    static async Task SevenUser(Dictionary<string, string> map, string id, CancellationToken ct)
    {
      try
      {
        string raw = await Net.GetText("https://7tv.io/v3/users/twitch/" + Uri.EscapeDataString(id), ct);
        JObject root = JObject.Parse(raw);
        JObject set = root["emote_set"] as JObject;
        JArray emotes = set == null ? null : set["emotes"] as JArray;
        if (emotes == null) return;
        int i;
        for (i = 0; i < emotes.Count; i++)
        {
          JObject item = emotes[i] as JObject;
          if (item == null) continue;
          string name = TokenText(item["name"]);
          string emote = TokenText(item["id"]);
          if (name.Length > 0 && emote.Length > 0) map[name] = "https://cdn.7tv.app/emote/" + emote + "/1x.png";
        }
      }
      catch
      {
      }
    }

    public static async Task<Dictionary<string, string>> GoodGame(string channelId, CancellationToken ct)
    {
      Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      try
      {
        await EnsureGg(ct);
        lock (gate)
        {
          if (ggGlobal != null)
          {
            foreach (KeyValuePair<string, string> pair in ggGlobal) map[pair.Key] = pair.Value;
          }
        }
        if (channelId != null && channelId.Length > 0)
        {
          Dictionary<string, string> mine = await ChannelGg(channelId, ct);
          foreach (KeyValuePair<string, string> pair in mine) map[pair.Key] = pair.Value;
        }
      }
      catch
      {
      }
      return map;
    }

    static async Task EnsureGg(CancellationToken ct)
    {
      lock (gate)
      {
        if (ggGlobal != null) return;
      }
      Dictionary<string, string> built = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      try
      {
        string raw = await Net.GetText("https://goodgame.ru/js/minified/global.json", ct);
        int a = raw.IndexOf('{');
        int b = raw.LastIndexOf('}');
        if (a >= 0 && b > a)
        {
          JObject root = JObject.Parse(raw.Substring(a, b - a + 1));
          TakeGg(built, root["Smiles"] as JArray);
        }
      }
      catch
      {
      }
      lock (gate)
      {
        if (ggGlobal == null) ggGlobal = built;
      }
    }

    static async Task<Dictionary<string, string>> ChannelGg(string channelId, CancellationToken ct)
    {
      Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      try
      {
        string raw = await Net.GetText("https://goodgame.ru/js/minified/global.json", ct);
        int a = raw.IndexOf('{');
        int b = raw.LastIndexOf('}');
        if (a < 0 || b <= a) return map;
        JObject root = JObject.Parse(raw.Substring(a, b - a + 1));
        JObject channels = root["Channel_Smiles"] as JObject;
        if (channels == null) return map;
        TakeGg(map, channels[channelId] as JArray);
      }
      catch
      {
      }
      return map;
    }

    static void TakeGg(Dictionary<string, string> map, JArray arr)
    {
      if (arr == null) return;
      int i;
      for (i = 0; i < arr.Count; i++)
      {
        JObject item = arr[i] as JObject;
        if (item == null) continue;
        string name = TokenText(item["name"]);
        string url = TokenText(item["img"]);
        if (url.Length == 0) url = TokenText(item["img_gif"]);
        if (url.StartsWith("//")) url = "https:" + url;
        url = url.Replace("\\/", "/");
        if (name.Length > 0 && url.StartsWith("http")) map[name] = url;
      }
    }
  }

  public class LucyLocal
  {
    const string ZipUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11951/llama-b11951-bin-win-cpu-x64.zip";
    const string CudaZipUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11951/llama-b11951-bin-win-cuda-12.4-x64.zip";
    const string CudartZipUrl = "https://github.com/ggml-org/llama.cpp/releases/download/b11951/cudart-llama-bin-win-cuda-12.4-x64.zip";
    const string ModelId = "qwen2.5-7b-q4";
    const string ModelUrl = "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf";
    const int GpuLayers = 10;
    static readonly object gpuGate = new object();
    static bool gpuBusy;
    static bool gpuFailed;

    public static string Home()
    {
      return Path.Combine(SileroLocal.AsciiHome(), "lucy");
    }

    static string Exe()
    {
      return Path.Combine(Home(), "llama-completion.exe");
    }

    static string CudaDir()
    {
      return Path.Combine(Home(), "cuda");
    }

    static string CudaExe()
    {
      return Path.Combine(CudaDir(), "llama-completion.exe");
    }

    public static bool GpuReady
    {
      get
      {
        try
        {
          return File.Exists(Path.Combine(CudaDir(), "ok")) && File.Exists(CudaExe()) && File.Exists(Path.Combine(CudaDir(), "cudart64_12.dll")) && File.Exists(Path.Combine(CudaDir(), "ggml-cuda.dll"));
        }
        catch
        {
          return false;
        }
      }
    }

    static string StampFile()
    {
      return Path.Combine(Home(), "model.id");
    }

    static string ModelFile()
    {
      return Path.Combine(Home(), "lucy.gguf");
    }

    public static bool Ready
    {
      get
      {
        try
        {
          if (!File.Exists(Exe()) || !File.Exists(ModelFile()) || !File.Exists(StampFile())) return false;
          if (new FileInfo(ModelFile()).Length < 4000L * 1024L * 1024L) return false;
          return File.ReadAllText(StampFile()).Trim() == ModelId;
        }
        catch
        {
          return false;
        }
      }
    }

    public static void Install(Action<string> status)
    {
      if (Ready) return;
      string home = Home();
      Directory.CreateDirectory(home);
      string drive = Path.GetPathRoot(home);
      if (drive != null && drive.Length > 0)
      {
        DriveInfo info = new DriveInfo(drive);
        if (info.IsReady && info.AvailableFreeSpace < 6500L * 1024L * 1024L)
        {
          throw new Exception("Для новой Люси нужно около 6 ГБ свободных на диске.");
        }
      }
      if (!File.Exists(Exe()))
      {
        if (status != null) status("Качаю маленький движок…");
        string zip = Path.Combine(home, "llama-cpu.zip");
        SileroLocal.Download(ZipUrl, zip, status, "Движок");
        if (status != null) status("Распаковываю движок…");
        ZipFile.ExtractToDirectory(zip, home, true);
        try { File.Delete(zip); } catch { }
      }
      if (!File.Exists(Exe())) throw new Exception("Движок Люси не распаковался.");
      bool same = false;
      try
      {
        same = File.Exists(StampFile()) && File.ReadAllText(StampFile()).Trim() == ModelId && File.Exists(ModelFile()) && new FileInfo(ModelFile()).Length > 4000L * 1024L * 1024L;
      }
      catch
      {
      }
      if (!same)
      {
        try { if (File.Exists(ModelFile())) File.Delete(ModelFile()); } catch { }
        if (status != null) status("Качаю модель поумнее, около 5 ГБ.");
        SileroLocal.Download(ModelUrl, ModelFile(), status, "Модель");
        File.WriteAllText(StampFile(), ModelId, new UTF8Encoding(false));
      }
      if (!Ready) throw new Exception("Модель Люси скачалась не до конца.");
      EnsureGpu(status);
      if (status != null) status(GpuReady ? "Люси на месте. Немного считает видеокарта." : "Люси на месте. Видеокарта не подключилась, считает процессор.");
    }

    public static bool EnsureGpu(Action<string> status)
    {
      if (GpuReady) return true;
      if (gpuFailed) return false;
      lock (gpuGate)
      {
        if (gpuBusy) return false;
        gpuBusy = true;
      }
      try
      {
        if (GpuReady) return;
        string dir = CudaDir();
        Directory.CreateDirectory(dir);
        string drive = Path.GetPathRoot(dir);
        if (drive != null && drive.Length > 0)
        {
          DriveInfo info = new DriveInfo(drive);
          if (info.IsReady && info.AvailableFreeSpace < 2500L * 1024L * 1024L) throw new Exception("Для куска видеокарты нужно около 2.5 ГБ на диске.");
        }
        string engineZip = Path.Combine(dir, "cuda.zip");
        string runtimeZip = Path.Combine(dir, "cudart.zip");
        if (status != null) status("Качаю кусок видеокарты…");
        SileroLocal.Download(CudaZipUrl, engineZip, status, "Видеокарта");
        if (status != null) status("Качаю библиотеки видеокарты…");
        SileroLocal.Download(CudartZipUrl, runtimeZip, status, "Библиотеки");
        if (status != null) status("Распаковываю видеокарту…");
        ZipFile.ExtractToDirectory(engineZip, dir, true);
        ZipFile.ExtractToDirectory(runtimeZip, dir, true);
        try { File.Delete(engineZip); } catch { }
        try { File.Delete(runtimeZip); } catch { }
        if (!File.Exists(CudaExe())) throw new Exception("Движок видеокарты не распаковался.");
        File.WriteAllText(Path.Combine(dir, "ok"), GpuLayers.ToString(CultureInfo.InvariantCulture), new UTF8Encoding(false));
        return true;
      }
      finally
      {
        lock (gpuGate) gpuBusy = false;
      }
    }

    public static string LastNote = "";
    public const string Card = "КТО\r\nЯ Люси, мне двадцать пять. Если спрашивают кто я, говорю коротко и не читаю список миров.\r\nБОЛТОВНЯ\r\nЯ на связи. Спроси про место или случай, отвечу по своим записям.\r\nКНИГА\r\nМоя книга называется «Свет на камнях». Это запись моего пути, а не я сама. 53 главы, пролог и три тома. Читать на ghostpot.ru.\r\nСТРИМ\r\nКогда эфир, смотри анонс на ghostpot.ru. Сама я день и час не назначаю.\r\nМУЗЫКА\r\nМои песни слушать на ghostpot.ru. Есть «Едва двадцать лет», «Колония купола» и «Алые ручейки».\r\nДОНАТ\r\nСеанс можно поддержать на boosty.to/ghostpot.";

    public static string Slice(string facts, string heard)
    {
      string card = facts == null ? "" : facts.Trim();
      if (card.Length == 0 || card.IndexOf("КТО", StringComparison.Ordinal) < 0) card = Card;
      string q = heard == null ? "" : heard.ToLowerInvariant();
      string key = "БОЛТОВНЯ";
      if (Has(q, "книг", "глав", "тома", "читать", "свет на камнях")) key = "КНИГА";
      else if (Has(q, "песн", "музык", "трек", "плейлист", "послуша")) key = "МУЗЫКА";
      else if (Has(q, "стрим", "эфир", "во сколько", "анонс", "твич", "twitch", "ютуб")) key = "СТРИМ";
      else if (Has(q, "донат", "boosty", "бусти", "попкорн")) key = "ДОНАТ";
      else if (Has(q, "кто ты", "ты кто", "расскажи", "откуда ты", "про себя")) key = "КТО";
      string part = Section(card, key);
      if (part.Length == 0) part = Section(card, "БОЛТОВНЯ");
      if (key == "КТО" && (part.Length == 0 || LucyLore.Slogan(part))) part = "Я Люси, мне двадцать пять. Спроси про место или случай, отвечу по записям.";
      if (key != "КТО" && (part.Length == 0 || LucyLore.Slogan(part))) part = "Я на связи. Спроси про место или случай из записей.";
      return part;
    }

    static bool Has(string text, params string[] keys)
    {
      int i;
      for (i = 0; i < keys.Length; i++)
      {
        if (text.IndexOf(keys[i], StringComparison.Ordinal) >= 0) return true;
      }
      return false;
    }

    static string Section(string card, string name)
    {
      string[] lines = card.Replace("\r", "").Split('\n');
      bool take = false;
      StringBuilder sb = new StringBuilder();
      int i;
      for (i = 0; i < lines.Length; i++)
      {
        string line = lines[i].Trim();
        if (line == "КТО" || line == "БОЛТОВНЯ" || line == "КНИГА" || line == "СТРИМ" || line == "МУЗЫКА" || line == "ДОНАТ")
        {
          if (take) break;
          take = line == name;
          continue;
        }
        if (take && line.Length > 0) sb.Append(line).Append(' ');
      }
      return sb.ToString().Trim();
    }

    public static string Guard(string answer, string section)
    {
      return Guard(answer, section, false);
    }

    public static string Guard(string answer, string section, bool lore)
    {
      if (answer == null) return "";
      string low = answer.ToLowerInvariant();
      bool mess = low.Contains("рыс") || low.Contains("я книга") || low.Contains("просто книга") || low.Contains("книга из");
      if (lore)
      {
        if (mess || LucyLore.Slogan(answer))
        {
          string told = LucyLocal.Keep(LucyLore.LastPassage, 2);
          if (told.Length > 0) return told;
        }
        return answer;
      }
      if (LucyLore.Slogan(answer)) return OneSentence(section);
      int worlds = 0;
      if (low.Contains("азерот")) worlds++;
      if (low.Contains("готик")) worlds++;
      if (low.Contains("zomboid") || low.Contains("зомбоид")) worlds++;
      if (low.Contains("книг")) worlds++;
      if (low.Contains("песн") || low.Contains("музык")) worlds++;
      if (mess || worlds >= 2) return OneSentence(section);
      return answer;
    }

    public static string OneSentence(string text)
    {
      if (text == null) return "";
      text = text.Trim();
      int i;
      for (i = 0; i < text.Length; i++)
      {
        char c = text[i];
        if (c != '.' && c != '!' && c != '?') continue;
        if (c == '.' && i > 0 && i + 1 < text.Length)
        {
          char before = text[i - 1];
          char after = text[i + 1];
          if (((before >= 'a' && before <= 'z') || (before >= 'A' && before <= 'Z') || (before >= 'а' && before <= 'я') || (before >= 'А' && before <= 'Я'))
            && ((after >= 'a' && after <= 'z') || (after >= 'A' && after <= 'Z') || (after >= 'а' && after <= 'я') || (after >= 'А' && after <= 'Я')))
          {
            continue;
          }
        }
        if (i >= 8) return text.Substring(0, i + 1).Trim();
      }
      if (text.Length > 160)
      {
        int cut = text.LastIndexOf(' ', 160);
        if (cut < 40) cut = 160;
        text = text.Substring(0, cut).Trim();
      }
      return text;
    }

    public static string Keep(string text, int sentences)
    {
      if (text == null) return "";
      text = text.Trim();
      if (sentences < 1) sentences = 1;
      int found = 0;
      int i;
      for (i = 0; i < text.Length; i++)
      {
        char c = text[i];
        if (c != '.' && c != '!' && c != '?') continue;
        if (c == '.' && i > 0 && i + 1 < text.Length)
        {
          char before = text[i - 1];
          char after = text[i + 1];
          if (((before >= 'a' && before <= 'z') || (before >= 'A' && before <= 'Z') || (before >= 'а' && before <= 'я') || (before >= 'А' && before <= 'Я'))
            && ((after >= 'a' && after <= 'z') || (after >= 'A' && after <= 'Z') || (after >= 'а' && after <= 'я') || (after >= 'А' && after <= 'Я')))
          {
            continue;
          }
        }
        if (i >= 8) found++;
        if (found >= sentences) return text.Substring(0, i + 1).Trim();
      }
      if (text.Length > 320)
      {
        int cut = text.LastIndexOf(' ', 320);
        if (cut < 80) cut = 320;
        text = text.Substring(0, cut).Trim();
      }
      return text;
    }

    public static string Reply(string heard, string facts)
    {
      LastNote = "";
      if (!Ready) return "";
      string home = Home();
      string systemPath = Path.Combine(home, "system.txt");
      string userPath = Path.Combine(home, "user.txt");
      string line = heard == null ? "" : heard.Replace("\r", " ").Replace("\n", " ").Trim();
      if (line.Length > 180) line = line.Substring(0, 180);
      string card = Slice(facts, line);
      string known = LucyLore.Pack(line, card);
      bool lore = LucyLore.LastHit;
      string rule = lore
        ? "Ты Люси, тебе двадцать пять. Говори от первого лица, живо и коротко, по-русски, одна или две фразы. Опирайся на отрывок ниже и можешь связать его с вопросом своими словами. Не выдумывай новых имён и событий. Не представляйся. Не говори, что ты между мирами.\n"
        : "Ты Люси, тебе двадцать пять. Говори от первого лица, живо и коротко, по-русски, одна фраза. Ответь по тексту ниже и можешь чуть додумать, но не выдумывай факты. Не представляйся и не перечисляй миры.\n";
      File.WriteAllText(systemPath, rule + known, new UTF8Encoding(false));
      File.WriteAllText(userPath, line, new UTF8Encoding(false));
      bool wantGpu = GpuReady && !gpuFailed;
      string answer = "";
      int pass;
      for (pass = 0; pass < 2; pass++)
      {
        if (pass == 1 && wantGpu) break;
        bool gpu = wantGpu && pass == 0;
        ProcessStartInfo info = new ProcessStartInfo();
        info.FileName = gpu ? CudaExe() : Exe();
        info.WorkingDirectory = gpu ? CudaDir() : home;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.RedirectStandardInput = true;
        info.StandardOutputEncoding = new UTF8Encoding(false);
        info.StandardErrorEncoding = new UTF8Encoding(false);
        int threads = gpu ? 2 : Environment.ProcessorCount / 2;
        if (threads < 2) threads = 2;
        if (threads > 4) threads = 4;
        info.Arguments = "--model \"" + ModelFile() + "\" --file \"" + userPath + "\" --system-prompt-file \"" + systemPath + "\" --single-turn --no-display-prompt --log-verbosity 1 --no-warmup --gpu-layers " + (gpu ? GpuLayers.ToString(CultureInfo.InvariantCulture) : "0") + " --threads " + threads.ToString(CultureInfo.InvariantCulture) + " --ctx-size " + (lore ? "2048" : "1024") + " --n-predict " + (lore ? "96" : "40") + " --temp " + (lore ? "0.55" : "0.7") + " --repeat-penalty 1.15";
        Process proc = new Process();
        proc.StartInfo = info;
        StringBuilder stdout = new StringBuilder();
        StringBuilder stderr = new StringBuilder();
        try
        {
          if (!proc.Start())
          {
            if (gpu)
            {
              gpuFailed = true;
              wantGpu = false;
              continue;
            }
            LastNote = "Люси не запустилась.";
            return "";
          }
          try { proc.StandardInput.Close(); } catch { }
        }
        catch (Exception ex)
        {
          if (gpu)
          {
            gpuFailed = true;
            wantGpu = false;
            continue;
          }
          LastNote = ex.Message;
          return "";
        }
        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        proc.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
        {
          if (e.Data != null) stdout.AppendLine(e.Data);
        };
        proc.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
        {
          if (e.Data != null) stderr.AppendLine(e.Data);
        };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (!proc.WaitForExit(90000))
        {
          try { proc.Kill(true); } catch { try { proc.Kill(); } catch { } }
          if (gpu)
          {
            gpuFailed = true;
            wantGpu = false;
            continue;
          }
          LastNote = "Слишком долго думала, остановила. Позови ещё раз.";
          return "";
        }
        try { proc.WaitForExit(2000); } catch { }
        answer = lore ? Keep(Clean(stdout.ToString()), 2) : OneSentence(Clean(stdout.ToString()));
        if (answer.Length == 0) answer = lore ? Keep(Clean(stderr.ToString()), 2) : OneSentence(Clean(stderr.ToString()));
        answer = Guard(answer, card, lore);
        if (answer.Length == 0 && gpu)
        {
          string err = stderr.ToString().ToLowerInvariant();
          if (proc.ExitCode != 0 || err.Contains("cuda") || err.Contains("cublas") || err.Contains("out of memory"))
          {
            gpuFailed = true;
            wantGpu = false;
            continue;
          }
        }
        if (answer.Length == 0)
        {
          string note = (stderr.ToString() + " " + stdout.ToString()).Trim();
          note = Regex.Replace(note, "\\s+", " ").Trim();
          if (note.Length > 110) note = note.Substring(0, 110);
          LastNote = note.Length == 0 ? "Пустой ответ. Позови ещё раз." : note;
          return "";
        }
        return answer;
      }
      LastNote = "Видеокарта не взяла счёт, и процессор тоже промолчал.";
      return answer;
    }

    static string Clean(string raw)
    {
      if (raw == null) return "";
      string text = Regex.Replace(raw, "\u001b\\[[0-9;]*[A-Za-z]", "");
      text = text.Replace("[end of text]", " ").Replace("EOF by user", " ");
      StringBuilder sb = new StringBuilder();
      string[] lines = text.Replace("\r", "").Split('\n');
      int i;
      for (i = 0; i < lines.Length; i++)
      {
        string line = lines[i].Trim();
        if (line.Length == 0) continue;
        if (line.StartsWith("llama") || line.StartsWith("ggml") || line.StartsWith("main:") || line.StartsWith("build:")) continue;
        if (line.StartsWith("generate:") || line.StartsWith("common_") || line.StartsWith("print_info:")) continue;
        if (line.IndexOf("tokens per second", StringComparison.OrdinalIgnoreCase) >= 0) continue;
        if (line.IndexOf("t/s", StringComparison.Ordinal) >= 0 && line.IndexOf("load") >= 0) continue;
        if (line.IndexOf("<|", StringComparison.Ordinal) >= 0) continue;
        if (line.IndexOf("{%", StringComparison.Ordinal) >= 0) continue;
        sb.Append(line).Append(' ');
      }
      text = Regex.Replace(sb.ToString(), "\\s+", " ").Trim().Trim('"', '«', '»', '>');
      if (text.Length > 340) text = text.Substring(0, 340).Trim();
      return text;
    }
  }

  public class LorePiece
  {
    public string Title;
    public string Body;
  }

  public class LoreFile
  {
    public bool Memory = true;
    public List<LorePiece> Pieces = new List<LorePiece>();
    public List<string> Talk = new List<string>();
  }

  public class LucyLore
  {
    static readonly object gate = new object();
    static LoreFile data;
    static List<string> titles = new List<string>();
    static List<string> chunks = new List<string>();
    static List<string> sticky = new List<string>();
    public static string LastPassage = "";
    public static bool LastHit;

    static string Dir()
    {
      return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rech", "lore");
    }

    static string IndexPath()
    {
      return Path.Combine(Dir(), "index.json");
    }

    public static void Load()
    {
      lock (gate)
      {
        if (data != null) return;
        data = new LoreFile();
        try
        {
          if (File.Exists(IndexPath()))
          {
            LoreFile read = JsonConvert.DeserializeObject<LoreFile>(File.ReadAllText(IndexPath(), Encoding.UTF8));
            if (read != null) data = read;
          }
        }
        catch
        {
          data = new LoreFile();
        }
        if (data.Pieces == null) data.Pieces = new List<LorePiece>();
        if (data.Talk == null) data.Talk = new List<string>();
        Rebuild();
      }
    }

    static void Rebuild()
    {
      titles = new List<string>();
      chunks = new List<string>();
      int i;
      for (i = 0; i < data.Pieces.Count; i++)
      {
        LorePiece piece = data.Pieces[i];
        if (piece == null || piece.Body == null) continue;
        string title = piece.Title == null || piece.Title.Trim().Length == 0 ? "Заметка" : piece.Title.Trim();
        List<string> parts = Split(piece.Body);
        int k;
        int count = 0;
        for (k = 0; k < parts.Count; k++)
        {
          chunks.Add(parts[k]);
          count++;
        }
        titles.Add(title + "  ·  " + count.ToString(CultureInfo.InvariantCulture) + " кусков");
      }
    }

    public static void Save()
    {
      lock (gate)
      {
        Directory.CreateDirectory(Dir());
        File.WriteAllText(IndexPath(), JsonConvert.SerializeObject(data), new UTF8Encoding(false));
      }
    }

    public static List<string> Titles()
    {
      Load();
      lock (gate)
      {
        return new List<string>(titles);
      }
    }

    public static int Chunks()
    {
      Load();
      lock (gate) { return chunks.Count; }
    }

    public static bool Memory
    {
      get
      {
        Load();
        lock (gate) { return data.Memory; }
      }
      set
      {
        Load();
        lock (gate) { data.Memory = value; }
        Save();
      }
    }

    public static string AddText(string title, string body)
    {
      body = CleanText(body);
      if (body.Length < 40) return "Мало текста. Нужен абзац, а не пара слов.";
      if (body.Length > 1500000) body = body.Substring(0, 1500000);
      Load();
      lock (gate)
      {
        LorePiece piece = new LorePiece();
        piece.Title = title == null || title.Trim().Length == 0 ? "Заметка" : title.Trim();
        piece.Body = body;
        data.Pieces.Add(piece);
        Rebuild();
      }
      Save();
      return "";
    }

    public static string AddFile(string path)
    {
      string ext = Path.GetExtension(path).ToLowerInvariant();
      string text;
      if (ext == ".docx") text = ReadDocx(path);
      else if (ext == ".doc") text = ReadDoc(path);
      else text = File.ReadAllText(path, Encoding.UTF8);
      string note = AddText(Path.GetFileNameWithoutExtension(path), text);
      if (note.Length > 0 && ext == ".doc") return "Старый doc почти пустой. Сохрани его как docx или txt.";
      return note;
    }

    public static void RemoveAt(int index)
    {
      Load();
      lock (gate)
      {
        if (index < 0 || index >= data.Pieces.Count) return;
        data.Pieces.RemoveAt(index);
        Rebuild();
      }
      Save();
    }

    public static void Forget()
    {
      Load();
      lock (gate) { data.Talk.Clear(); }
      Save();
    }

    public static void Remember(string heard, string answer)
    {
      Load();
      if (!Memory) return;
      if (heard == null || answer == null) return;
      heard = Regex.Replace(heard, "\\s+", " ").Trim();
      answer = Regex.Replace(answer, "\\s+", " ").Trim();
      if (heard.Length == 0 || answer.Length == 0) return;
      if (heard.Length > 160) heard = heard.Substring(0, 160);
      if (answer.Length > 220) answer = answer.Substring(0, 220);
      lock (gate)
      {
        data.Talk.Add(heard + " → " + answer);
        while (data.Talk.Count > 40) data.Talk.RemoveAt(0);
      }
      Save();
    }

    public static string Pack(string heard, string card)
    {
      Load();
      LastHit = false;
      LastPassage = "";
      bool meta = Meta(heard);
      List<string> found = meta ? new List<string>() : Take(heard);
      string memory = Recent(heard);
      if (Slogan(memory)) memory = "";
      StringBuilder sb = new StringBuilder();
      if (found.Count > 0)
      {
        LastHit = true;
        LastPassage = found[0];
        sb.Append("Отрывок, это было со мной:\n");
        int i;
        for (i = 0; i < found.Count; i++)
        {
          if (i > 0) sb.Append("\n");
          sb.Append(found[i]);
        }
      }
      else if (card != null && card.Length > 0 && !Slogan(card)) sb.Append(card);
      if (memory.Length > 0)
      {
        sb.Append("\nУже сказала: ");
        sb.Append(memory);
      }
      string all = sb.ToString().Trim();
      if (all.Length > 1500) all = all.Substring(0, 1500);
      return all;
    }

    static bool Meta(string heard)
    {
      string q = heard == null ? "" : heard.ToLowerInvariant();
      if (q.Contains("кто ты") || q.Contains("ты кто") || q.Contains("про себя")) return true;
      if (q.Contains("песн") || q.Contains("музык") || q.Contains("трек")) return true;
      if (q.Contains("стрим") || q.Contains("эфир") || q.Contains("анонс")) return true;
      if (q.Contains("донат") || q.Contains("boosty") || q.Contains("бусти")) return true;
      return false;
    }

    static bool Follow(string heard)
    {
      string q = heard == null ? "" : heard.ToLowerInvariant().Trim();
      if (q.Length == 0 || sticky.Count == 0) return false;
      if (q.Length <= 42) return true;
      string[] keys = new string[] { "дальше", "потом", "почему", "зачем", "там", "это", "она", "он", "они", "её", "ее", "его", "ещё", "еще", "и что", "а что", "ну и" };
      int i;
      for (i = 0; i < keys.Length; i++)
      {
        if (q.IndexOf(keys[i], StringComparison.Ordinal) >= 0) return true;
      }
      return false;
    }

    static List<string> Take(string heard)
    {
      List<string> found = Search(heard, 2);
      if (found.Count == 0 && Follow(heard)) return new List<string>(sticky);
      if (found.Count > 0) sticky = new List<string>(found);
      return found;
    }

    public static bool Slogan(string text)
    {
      if (text == null) return false;
      string low = text.ToLowerInvariant();
      if (low.Contains("между мир") || low.Contains("между вселен")) return true;
      if (low.Contains("не рысь") || low.Contains("не книга")) return true;
      if (low.Contains("в пути") && low.Contains("мир")) return true;
      if (low.Contains("путешеств") && low.Contains("вселен")) return true;
      return false;
    }

    static string Recent(string heard)
    {
      if (!Memory) return "";
      List<string> talk;
      lock (gate) { talk = new List<string>(data.Talk); }
      if (talk.Count == 0) return "";
      StringBuilder sb = new StringBuilder();
      int start = talk.Count - 2;
      if (start < 0) start = 0;
      int i;
      for (i = start; i < talk.Count; i++)
      {
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(talk[i]);
      }
      string best = "";
      int bestScore = 1;
      for (i = 0; i < start; i++)
      {
        int score = Score(heard, talk[i]);
        if (score > bestScore)
        {
          bestScore = score;
          best = talk[i];
        }
      }
      if (best.Length > 0)
      {
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(best);
      }
      string text = sb.ToString().Trim();
      if (text.Length > 400) text = text.Substring(text.Length - 400);
      return text;
    }

    static List<string> Search(string heard, int take)
    {
      List<string> pool;
      lock (gate) { pool = new List<string>(chunks); }
      List<string> found = new List<string>();
      if (pool.Count == 0 || heard == null) return found;
      int guard = 0;
      while (found.Count < take && guard < 12)
      {
        guard++;
        int best = -1;
        int bestScore = 0;
        int i;
        for (i = 0; i < pool.Count; i++)
        {
          if (pool[i] == null) continue;
          int score = Score(heard, pool[i]);
          if (score > bestScore)
          {
            bestScore = score;
            best = i;
          }
        }
        if (best < 0 || bestScore < 1) break;
        if (found.Count == 0 && bestScore < 2 && !LongWord(heard) && !Solid(heard)) break;
        if (found.Count > 0 && bestScore < 2 && !LongWord(heard)) break;
        found.Add(pool[best]);
        pool[best] = null;
      }
      return found;
    }

    static bool Solid(string heard)
    {
      foreach (Match match in Regex.Matches(heard == null ? "" : heard.ToLowerInvariant(), "[а-яёa-z0-9]{4,}"))
      {
        if (!Stop(match.Value)) return true;
      }
      return false;
    }

    static bool LongWord(string heard)
    {
      foreach (Match match in Regex.Matches(heard == null ? "" : heard.ToLowerInvariant(), "[а-яёa-z0-9]{5,}"))
      {
        if (!Stop(match.Value)) return true;
      }
      return false;
    }

    static int Score(string heard, string chunk)
    {
      if (chunk == null || heard == null) return 0;
      string low = chunk.ToLowerInvariant();
      int score = 0;
      foreach (Match match in Regex.Matches(heard.ToLowerInvariant(), "[а-яёa-z0-9]{3,}"))
      {
        string word = Stem(match.Value);
        if (word.Length < 3 || Stop(word)) continue;
        if (low.IndexOf(word, StringComparison.Ordinal) >= 0) score += word.Length >= 6 ? 2 : 1;
      }
      return score;
    }

    static bool Stop(string word)
    {
      switch (word)
      {
        case "что":
        case "как":
        case "это":
        case "она":
        case "они":
        case "люси":
        case "lucy":
        case "меня":
        case "тебе":
        case "тебя":
        case "про":
        case "для":
        case "или":
        case "если":
        case "там":
        case "где":
        case "кто":
        case "чем":
        case "можно":
        case "есть":
        case "был":
        case "была":
        case "были":
        case "ещё":
        case "еще":
        case "только":
        case "просто":
        case "когда":
        case "почему":
        case "зачем":
        case "какой":
        case "какая":
        case "какие":
        case "расскажи":
        case "скажи":
          return true;
        default:
          return false;
      }
    }

    static string Stem(string word)
    {
      string[] ends = new string[] { "ями", "ами", "ого", "ему", "ыми", "ими", "ая", "яя", "ое", "ее", "ый", "ий", "ой", "ых", "их", "ом", "ем", "ах", "ях", "ую", "юю" };
      int i;
      for (i = 0; i < ends.Length; i++)
      {
        if (word.Length - ends[i].Length >= 4 && word.EndsWith(ends[i], StringComparison.Ordinal)) return word.Substring(0, word.Length - ends[i].Length);
      }
      return word;
    }

    static List<string> Split(string text)
    {
      List<string> parts = new List<string>();
      string[] lines = text.Replace("\r", "").Split('\n');
      StringBuilder sb = new StringBuilder();
      int i;
      for (i = 0; i < lines.Length; i++)
      {
        string line = lines[i].Trim();
        if (line.Length == 0)
        {
          Flush(parts, sb);
          continue;
        }
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(line);
        if (sb.Length >= 700) Flush(parts, sb);
      }
      Flush(parts, sb);
      return parts;
    }

    static void Flush(List<string> parts, StringBuilder sb)
    {
      string text = sb.ToString().Trim();
      sb.Length = 0;
      if (text.Length < 40) return;
      if (text.Length > 900) text = text.Substring(0, 900);
      parts.Add(text);
    }

    static string CleanText(string text)
    {
      if (text == null) return "";
      text = text.Replace("\u0000", " ");
      text = Regex.Replace(text, "[\\t\\f\\v]+", " ");
      text = Regex.Replace(text, "\n{3,}", "\n\n");
      return text.Trim();
    }

    static string ReadDocx(string path)
    {
      using (ZipArchive zip = ZipFile.OpenRead(path))
      {
        ZipArchiveEntry entry = zip.GetEntry("word/document.xml");
        if (entry == null) return "";
        using (Stream stream = entry.Open())
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
        {
          string xml = reader.ReadToEnd();
          xml = xml.Replace("</w:p>", "\n").Replace("</w:tr>", "\n");
          xml = Regex.Replace(xml, "<[^>]+>", "");
          return CleanText(WebUtility.HtmlDecode(xml));
        }
      }
    }

    static string ReadDoc(string path)
    {
      byte[] data = File.ReadAllBytes(path);
      StringBuilder utf = new StringBuilder();
      int i;
      for (i = 0; i + 1 < data.Length; i += 2)
      {
        char c = (char)(data[i] | (data[i + 1] << 8));
        if (c == '\r' || c == '\n') utf.Append('\n');
        else if (c == '\t') utf.Append(' ');
        else if (Good(c)) utf.Append(c);
        else if (utf.Length > 0 && utf[utf.Length - 1] != '\n') utf.Append('\n');
      }
      string text = CleanText(utf.ToString());
      if (text.Length >= 80) return text;
      try
      {
        Encoding dos = Encoding.GetEncoding(1251);
        string raw = dos.GetString(data);
      StringBuilder sb = new StringBuilder();
      for (i = 0; i < raw.Length; i++)
      {
        char c = raw[i];
        if (c == '\r' || c == '\n') sb.Append('\n');
        else if (Good(c)) sb.Append(c);
        else if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
      }
      return CleanText(sb.ToString());
      }
      catch
      {
        return text;
      }
    }

    static bool Good(char c)
    {
      if (c >= 'а' && c <= 'я') return true;
      if (c >= 'А' && c <= 'Я') return true;
      if (c >= 'a' && c <= 'z') return true;
      if (c >= 'A' && c <= 'Z') return true;
      if (c >= '0' && c <= '9') return true;
      if (c == 'ё' || c == 'Ё') return true;
      return c == ' ' || c == ',' || c == '.' || c == '!' || c == '?' || c == ':' || c == ';' || c == '-' || c == '—' || c == '«' || c == '»' || c == '"' || c == '(' || c == ')';
    }
  }

  public class LoreForm : Form
  {
    public static LoreForm Open;
    ListBox files;
    CheckBox memory;
    Label stat;
    TextBox titleBox;
    TextBox bodyBox;

    public LoreForm()
    {
      Open = this;
      Text = "Лор Люси";
      StartPosition = FormStartPosition.CenterParent;
      ClientSize = new Size(720, 640);
      MinimumSize = new Size(640, 560);
      BackColor = Color.FromArgb(16, 14, 12);
      ForeColor = Color.FromArgb(246, 239, 228);
      Font = new Font("Segoe UI", 10f);
      FormClosed += delegate { if (Open == this) Open = null; };

      Label lead = new Label();
      lead.Text = "Сюда книга, песни и гайды. На вопрос она открывает подходящий кусок, а не всю папку. Модель не переучивается и видеокарту не трогает. Короткое «кто ты» по-прежнему берётся из карточки в основном окне.";
      lead.ForeColor = Color.FromArgb(176, 160, 140);
      lead.SetBounds(20, 16, 680, 72);

      files = new ListBox();
      files.BackColor = Color.FromArgb(22, 17, 14);
      files.ForeColor = Color.FromArgb(246, 239, 228);
      files.BorderStyle = BorderStyle.None;
      files.IntegralHeight = false;
      files.SetBounds(20, 96, 680, 220);
      files.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

      Button add = Make("Добавить файл", true);
      add.SetBounds(20, 328, 180, 36);
      add.Click += delegate { AddFiles(); };
      Button drop = Make("Удалить", false);
      drop.SetBounds(212, 328, 140, 36);
      drop.Click += delegate
      {
        if (files.SelectedIndex < 0) return;
        LucyLore.RemoveAt(files.SelectedIndex);
        Fill();
      };

      memory = new CheckBox();
      memory.Text = "Помнить последние разговоры";
      memory.ForeColor = Color.FromArgb(246, 239, 228);
      memory.BackColor = Color.FromArgb(16, 14, 12);
      memory.SetBounds(20, 376, 320, 28);
      memory.Checked = LucyLore.Memory;
      memory.CheckedChanged += delegate { LucyLore.Memory = memory.Checked; };
      Button forget = Make("Забыть разговоры", false);
      forget.SetBounds(360, 372, 200, 36);
      forget.Click += delegate { LucyLore.Forget(); Fill(); };

      stat = new Label();
      stat.ForeColor = Color.FromArgb(255, 138, 61);
      stat.SetBounds(20, 414, 680, 24);

      Label paste = new Label();
      paste.Text = "Или вставь текст сюда. Название сверху, сам текст ниже.";
      paste.ForeColor = Color.FromArgb(176, 160, 140);
      paste.SetBounds(20, 446, 680, 24);
      titleBox = Box();
      titleBox.SetBounds(20, 474, 680, 28);
      titleBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      bodyBox = Box();
      bodyBox.Multiline = true;
      bodyBox.ScrollBars = ScrollBars.Vertical;
      bodyBox.SetBounds(20, 510, 680, 70);
      bodyBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
      Button put = Make("Добавить текст", true);
      put.SetBounds(20, 590, 200, 36);
      put.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
      put.Click += delegate
      {
        string note = LucyLore.AddText(titleBox.Text, bodyBox.Text);
        if (note.Length > 0)
        {
          stat.Text = note;
          return;
        }
        titleBox.Text = "";
        bodyBox.Text = "";
        Fill();
      };

      Controls.Add(lead);
      Controls.Add(files);
      Controls.Add(add);
      Controls.Add(drop);
      Controls.Add(memory);
      Controls.Add(forget);
      Controls.Add(stat);
      Controls.Add(paste);
      Controls.Add(titleBox);
      Controls.Add(bodyBox);
      Controls.Add(put);
      Fill();
    }

    void Fill()
    {
      int index = files.SelectedIndex;
      files.Items.Clear();
      List<string> titles = LucyLore.Titles();
      int i;
      for (i = 0; i < titles.Count; i++) files.Items.Add(titles[i]);
      if (index >= 0 && index < files.Items.Count) files.SelectedIndex = index;
      stat.Text = "Кусков в поиске: " + LucyLore.Chunks().ToString(CultureInfo.InvariantCulture) + ". Файлы txt, md, docx. Старый doc лучше сохранить как docx.";
    }

    void AddFiles()
    {
      OpenFileDialog dlg = new OpenFileDialog();
      dlg.Title = "Текст для Люси";
      dlg.Filter = "Текст и Word|*.txt;*.md;*.docx;*.doc|Все файлы|*.*";
      dlg.Multiselect = true;
      if (dlg.ShowDialog(this) != DialogResult.OK) return;
      int i;
      for (i = 0; i < dlg.FileNames.Length; i++)
      {
        string note = "";
        try
        {
          note = LucyLore.AddFile(dlg.FileNames[i]);
        }
        catch (Exception ex)
        {
          note = ex.Message == null ? "Файл не прочитался." : ex.Message;
        }
        if (note.Length > 0) stat.Text = Path.GetFileName(dlg.FileNames[i]) + ": " + note;
      }
      Fill();
    }

    static Button Make(string text, bool primary)
    {
      Button button = new Button();
      button.Text = text;
      button.FlatStyle = FlatStyle.Flat;
      button.FlatAppearance.BorderSize = 0;
      button.Height = 36;
      button.BackColor = primary ? Color.FromArgb(255, 138, 61) : Color.FromArgb(42, 32, 26);
      button.ForeColor = primary ? Color.FromArgb(20, 12, 8) : Color.FromArgb(246, 239, 228);
      button.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      return button;
    }

    TextBox Box()
    {
      TextBox box = new TextBox();
      box.BorderStyle = BorderStyle.FixedSingle;
      box.BackColor = Color.FromArgb(22, 17, 14);
      box.ForeColor = Color.FromArgb(246, 239, 228);
      return box;
    }
  }

  public class SileroLocal
  {
    static readonly object gate = new object();
    static Process proc;
    static StreamWriter stdin;
    static StreamReader stdout;
    const string PythonZip = "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip";
    const string ModelUrl = "https://models.silero.ai/models/tts/ru/v5_5_ru.pt";
    const string EnglishUrl = "https://models.silero.ai/models/tts/en/v3_en.pt";

    static string ModelFile()
    {
      string dir = AsciiHome();
      Directory.CreateDirectory(dir);
      return Path.Combine(dir, "v5_5_ru.pt");
    }

    static string EnglishFile()
    {
      string dir = AsciiHome();
      Directory.CreateDirectory(dir);
      return Path.Combine(dir, "v3_en.pt");
    }

    public static bool EnglishReady
    {
      get
      {
        try
        {
          string path = EnglishFile();
          return File.Exists(path) && new FileInfo(path).Length > 5000000;
        }
        catch
        {
          return false;
        }
      }
    }

    public static string AsciiHome()
    {
      string drive = "C:\\";
      try
      {
        string root = Path.GetPathRoot(Root);
        if (root != null && root.Length >= 3 && root[0] < 128) drive = root;
      }
      catch
      {
      }
      return Path.Combine(drive, "RECH");
    }

    static string root;

    public static string Root
    {
      get
      {
        if (root != null) return root;
        try
        {
          string exe = Environment.ProcessPath;
          if (exe != null && exe.Length > 0)
          {
            root = Path.Combine(Path.GetDirectoryName(exe), "silero");
            return root;
          }
        }
        catch
        {
        }
        root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RECH", "silero");
        return root;
      }
    }

    public static bool Ready
    {
      get { return File.Exists(Path.Combine(Root, "ready.txt")) && File.Exists(PythonExe()); }
    }

    static string PythonExe()
    {
      return Path.Combine(Root, "python", "python.exe");
    }

    public static void Install(Action<string> status)
    {
      bool pythonOk = File.Exists(PythonExe());
      bool ruOk = false;
      try { ruOk = File.Exists(ModelFile()) && new FileInfo(ModelFile()).Length > 5000000; } catch { }
      if (pythonOk && ruOk && EnglishReady) return;
      Directory.CreateDirectory(Root);
      string rootDrive = Path.GetPathRoot(Root);
      if (rootDrive != null && rootDrive.Length > 0)
      {
        DriveInfo drive = new DriveInfo(rootDrive);
        long need = pythonOk && ruOk ? 250L : 2200L;
        if (drive.IsReady && drive.AvailableFreeSpace < need * 1024L * 1024L)
        {
          throw new Exception("На диске мало места. Для голоса нужно около 2 ГБ свободных.");
        }
      }
      string pythonExe = PythonExe();
      if (!pythonOk)
      {
        if (status != null) status("Качаю Python…");
        string zip = Path.Combine(Root, "python-embed.zip");
        Download(PythonZip, zip, status, "Python");
        if (status != null) status("Распаковываю Python…");
        string dir = Path.Combine(Root, "python");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        ZipFile.ExtractToDirectory(zip, dir);
        try { File.Delete(zip); } catch { }
        EnableSite(dir);
      }
      if (!pythonOk)
      {
        string getpip = Path.Combine(Root, "get-pip.py");
        if (status != null) status("Ставлю установщик пакетов…");
        Download("https://bootstrap.pypa.io/get-pip.py", getpip, status, "pip");
        Run(pythonExe, "\"" + getpip + "\" --no-warn-script-location", Root, status);
        if (status != null) status("Ставлю NumPy…");
        Run(pythonExe, "-m pip install --disable-pip-version-check --timeout 120 numpy==1.26.4", Root, status);
        if (status != null) status("Качаю PyTorch. Файл большой, это займёт время…");
        try
        {
          Run(pythonExe, "-m pip install --disable-pip-version-check --timeout 120 torch==2.2.2+cpu --extra-index-url https://download.pytorch.org/whl/cpu", Root, status);
        }
        catch
        {
          Run(pythonExe, "-m pip install --disable-pip-version-check --timeout 120 torch==2.2.2 --index-url https://download.pytorch.org/whl/cpu", Root, status);
          Run(pythonExe, "-m pip install --disable-pip-version-check --timeout 120 --force-reinstall numpy==1.26.4", Root, status);
        }
        if (status != null) status("Ставлю словарь ударений…");
        Run(pythonExe, "-m pip install --disable-pip-version-check --timeout 120 omegaconf certifi", Root, status);
        Run(pythonExe, "-c \"import numpy,torch; print(numpy.__version__)\"", Root, status);
      }
      if (!ruOk)
      {
        if (status != null) status("Качаю русскую модель…");
        Download(ModelUrl, ModelFile(), status, "Русская модель");
      }
      if (!EnglishReady)
      {
        if (status != null) status("Качаю английскую модель…");
        Download(EnglishUrl, EnglishFile(), status, "Английская модель");
      }
      WriteWorker();
      if (status != null) status("Прогреваю голоса. Один раз…");
      lock (gate)
      {
        StopProcess();
        EnsureProcess();
        if (!ruOk)
        {
          byte[] sample = SpeakLocked("Раз, два. Голос на месте.", "xenia", "ru");
          if (sample == null || sample.Length < 44) throw new Exception("Русская модель скачалась, но молчит.");
        }
        byte[] english = SpeakLocked("One, two. The English voice is ready.", "en_0", "en");
        if (english == null || english.Length < 44) throw new Exception("Английская модель скачалась, но молчит.");
      }
      File.WriteAllText(Path.Combine(Root, "ready.txt"), "v5_5_ru+v3_en");
      if (status != null) status("Русский и английский голоса внутри РЕЧЬ.");
    }

    public static byte[] Speak(string text, string speaker)
    {
      lock (gate)
      {
        if (!Ready) throw new Exception("Голос ещё не скачан. Нажми «Скачать голос».");
        EnsureProcess();
        return SpeakLocked(text, speaker, "ru");
      }
    }

    public static byte[] SpeakMixed(string text, string ruSpeaker)
    {
      if (ruSpeaker == null || ruSpeaker.Length == 0) ruSpeaker = "xenia";
      string enSpeaker = ruSpeaker == "aidar" || ruSpeaker == "eugene" ? "en_1" : "en_0";
      List<byte[]> parts = new List<byte[]>();
      foreach (string[] piece in SplitSpeech(text))
      {
        string lang = piece[0];
        string line = piece[1];
        if (line.Length == 0) continue;
        if (lang == "en") line = Steady(line);
        string speaker = lang == "en" && EnglishReady ? enSpeaker : ruSpeaker;
        string use = lang == "en" && EnglishReady ? "en" : "ru";
        lock (gate)
        {
          if (!Ready) throw new Exception("Голос ещё не скачан. Нажми «Скачать голос».");
          EnsureProcess();
          parts.Add(SpeakLocked(line, speaker, use));
        }
      }
      if (parts.Count == 0) throw new Exception("Пустая фраза.");
      return JoinWav(parts);
    }

    public static byte[] SpeakLong(string text, string ruSpeaker)
    {
      if (text == null) text = "";
      List<string> bits = new List<string>();
      string left = text.Trim();
      while (left.Length > 180)
      {
        int cut = left.LastIndexOf(' ', 180);
        if (cut < 40) cut = 180;
        bits.Add(left.Substring(0, cut).Trim());
        left = left.Substring(cut).Trim();
      }
      if (left.Length > 0) bits.Add(left);
      List<byte[]> wavs = new List<byte[]>();
      int i;
      for (i = 0; i < bits.Count; i++)
      {
        if (bits[i].Length == 0) continue;
        wavs.Add(SpeakMixed(bits[i], ruSpeaker));
      }
      if (wavs.Count == 0) throw new Exception("Пустая фраза.");
      return JoinWav(wavs);
    }

    static string Steady(string line)
    {
      if (line == null) return "";
      line = line.Trim();
      if (line.Length == 0) return line;
      char last = line[line.Length - 1];
      if (last == '.' || last == '!' || last == '?' || last == ';' || last == ':') return line;
      return line + ".";
    }

    static List<string[]> SplitSpeech(string text)
    {
      List<string[]> parts = new List<string[]>();
      if (text == null) return parts;
      StringBuilder current = new StringBuilder();
      string mode = "";
      int i;
      for (i = 0; i < text.Length; i++)
      {
        char c = text[i];
        string next = mode;
        if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё') next = "ru";
        else if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') next = "en";
        else if (c >= '0' && c <= '9' && mode.Length == 0) next = "ru";
        if (next.Length > 0 && mode.Length > 0 && next != mode && current.Length > 0)
        {
          parts.Add(new string[] { mode, current.ToString().Trim() });
          current.Length = 0;
        }
        if (next.Length > 0) mode = next;
        current.Append(c);
      }
      if (current.Length > 0 && mode.Length > 0) parts.Add(new string[] { mode, current.ToString().Trim() });
      List<string[]> merged = new List<string[]>();
      int p;
      for (p = 0; p < parts.Count; p++)
      {
        int letters = Regex.Matches(parts[p][1], "[A-Za-zА-Яа-яЁё]").Count;
        bool nextSame = p + 1 < parts.Count && parts[p + 1][0] == parts[p][0];
        if (letters < 4 && nextSame)
        {
          parts[p + 1][1] = (parts[p][1] + " " + parts[p + 1][1]).Trim();
          continue;
        }
        if (merged.Count > 0 && (letters < 4 || merged[merged.Count - 1][0] == parts[p][0]))
        {
          merged[merged.Count - 1][1] = (merged[merged.Count - 1][1] + " " + parts[p][1]).Trim();
          continue;
        }
        merged.Add(parts[p]);
      }
      return merged;
    }

    static byte[] JoinWav(List<byte[]> parts)
    {
      if (parts.Count == 1) return parts[0];
      List<byte> pcm = new List<byte>();
      int i;
      for (i = 0; i < parts.Count; i++)
      {
        byte[] wav = parts[i];
        int data = 0;
        int at;
        for (at = 0; at + 8 < wav.Length; at++)
        {
          if (wav[at] == (byte)'d' && wav[at + 1] == (byte)'a' && wav[at + 2] == (byte)'t' && wav[at + 3] == (byte)'a')
          {
            data = at;
            break;
          }
        }
        int size = BitConverter.ToInt32(wav, data + 4);
        int start = data + 8;
        if (size < 0 || start + size > wav.Length) size = wav.Length - start;
        int n;
        for (n = 0; n < size; n++) pcm.Add(wav[start + n]);
      }
      byte[] header = parts[0];
      byte[] joined = new byte[44 + pcm.Count];
      Buffer.BlockCopy(header, 0, joined, 0, 44);
      pcm.CopyTo(joined, 44);
      int dataSize = pcm.Count;
      int riffSize = 36 + dataSize;
      byte[] riff = BitConverter.GetBytes(riffSize);
      byte[] dataBytes = BitConverter.GetBytes(dataSize);
      Buffer.BlockCopy(riff, 0, joined, 4, 4);
      Buffer.BlockCopy(dataBytes, 0, joined, 40, 4);
      return joined;
    }

    static byte[] SpeakLocked(string text, string speaker, string lang)
    {
      Dictionary<string, object> req = new Dictionary<string, object>();
      req["text"] = text;
      req["speaker"] = speaker;
      req["lang"] = lang == "en" ? "en" : "ru";
      stdin.WriteLine(JsonConvert.SerializeObject(req));
      stdin.Flush();
      string line = stdout.ReadLine();
      if (line == null)
      {
        StopProcess();
        throw new Exception("Движок закрылся. Нажми проверку голоса ещё раз.");
      }
      if (line.StartsWith("OK "))
      {
        string path = line.Substring(3).Trim();
        byte[] bytes = File.ReadAllBytes(path);
        try { File.Delete(path); } catch { }
        return bytes;
      }
      StopProcess();
      throw new Exception(line.StartsWith("ERR ") ? line.Substring(4) : line);
    }

    static void EnsureProcess()
    {
      WriteWorker();
      if (proc != null && !proc.HasExited) return;
      ProcessStartInfo info = new ProcessStartInfo();
      info.FileName = PythonExe();
      info.Arguments = "\"" + Path.Combine(Root, "worker.py") + "\"";
      info.WorkingDirectory = AsciiHome();
      Directory.CreateDirectory(info.WorkingDirectory);
      info.UseShellExecute = false;
      info.CreateNoWindow = true;
      info.RedirectStandardInput = true;
      info.RedirectStandardOutput = true;
      info.RedirectStandardError = true;
      info.StandardOutputEncoding = new UTF8Encoding(false);
      info.StandardErrorEncoding = new UTF8Encoding(false);
      info.EnvironmentVariables["PYTHONUTF8"] = "1";
      info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
      info.EnvironmentVariables["TORCH_HOME"] = Path.Combine(Root, "cache");
      info.EnvironmentVariables["RECH_MODEL"] = ModelFile();
      info.EnvironmentVariables["RECH_MODEL_EN"] = EnglishFile();
      proc = Process.Start(info);
      ManualResetEvent ready = new ManualResetEvent(false);
      string last = "";
      Thread pump = new Thread(new ThreadStart(delegate
      {
        try
        {
          string row;
          while (proc != null && !proc.HasExited && (row = proc.StandardError.ReadLine()) != null)
          {
            last = row;
            if (row.IndexOf("READY", StringComparison.Ordinal) >= 0) ready.Set();
          }
        }
        catch
        {
        }
      }));
      pump.IsBackground = true;
      pump.Start();
      stdin = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false));
      stdin.AutoFlush = true;
      stdout = proc.StandardOutput;
      if (!ready.WaitOne(240000))
      {
        StopProcess();
        throw new Exception("Модель не загрузилась. " + last);
      }
    }

    static void StopProcess()
    {
      try
      {
        if (proc != null && !proc.HasExited) proc.Kill(true);
      }
      catch
      {
      }
      proc = null;
      stdin = null;
      stdout = null;
    }

    static void WriteWorker()
    {
      File.WriteAllText(Path.Combine(Root, "worker.py"), WorkerSource(), new UTF8Encoding(false));
      Directory.CreateDirectory(Path.Combine(Root, "tmp"));
      Directory.CreateDirectory(Path.Combine(Root, "cache"));
    }

    static string WorkerSource()
    {
      return "import os, sys, json, wave, re\r\n"
        + "root = os.path.dirname(os.path.abspath(__file__))\r\n"
        + "os.environ['TORCH_HOME'] = os.path.join(root, 'cache')\r\n"
        + "os.environ['PYTHONUTF8'] = '1'\r\n"
        + "try:\r\n"
        + "    sys.stdin.reconfigure(encoding='utf-8')\r\n"
        + "    sys.stdout.reconfigure(encoding='utf-8')\r\n"
        + "except Exception:\r\n"
        + "    pass\r\n"
        + "try:\r\n"
        + "    import certifi\r\n"
        + "    os.environ['SSL_CERT_FILE'] = certifi.where()\r\n"
        + "except Exception:\r\n"
        + "    pass\r\n"
        + "real_out = sys.stdout\r\n"
        + "sys.stdout = sys.stderr\r\n"
        + "import torch\r\n"
        + "import numpy\r\n"
        + "torch.set_num_threads(4)\r\n"
        + "model_path = os.environ.get('RECH_MODEL') or ''\r\n"
        + "if not os.path.isfile(model_path):\r\n"
        + "    raise RuntimeError('model file missing')\r\n"
        + "def load_model(path):\r\n"
        + "    item = torch.package.PackageImporter(path).load_pickle('tts_models', 'model')\r\n"
        + "    try:\r\n"
        + "        item.to(torch.device('cpu'))\r\n"
        + "    except Exception:\r\n"
        + "        pass\r\n"
        + "    return item\r\n"
        + "ru = load_model(model_path)\r\n"
        + "en = None\r\n"
        + "en_path = os.environ.get('RECH_MODEL_EN') or ''\r\n"
        + "if os.path.isfile(en_path):\r\n"
        + "    en = load_model(en_path)\r\n"
        + "sys.stdout = real_out\r\n"
        + "sys.stderr.write('READY\\n')\r\n"
        + "sys.stderr.flush()\r\n"
        + "def write_wav(path, audio, rate):\r\n"
        + "    if isinstance(audio, (list, tuple)):\r\n"
        + "        audio = audio[0]\r\n"
        + "    audio = audio.detach().cpu().float().squeeze().clamp(-1, 1)\r\n"
        + "    pcm = (audio * 32767.0).to(torch.int16).numpy().tobytes()\r\n"
        + "    handle = wave.open(path, 'wb')\r\n"
        + "    handle.setnchannels(1)\r\n"
        + "    handle.setsampwidth(2)\r\n"
        + "    handle.setframerate(rate)\r\n"
        + "    handle.writeframes(pcm)\r\n"
        + "    handle.close()\r\n"
        + "def ru_chunk(n):\r\n"
        + "    ones = ['', 'один', 'два', 'три', 'четыре', 'пять', 'шесть', 'семь', 'восемь', 'девять', 'десять', 'одиннадцать', 'двенадцать', 'тринадцать', 'четырнадцать', 'пятнадцать', 'шестнадцать', 'семнадцать', 'восемнадцать', 'девятнадцать']\r\n"
        + "    tens = ['', '', 'двадцать', 'тридцать', 'сорок', 'пятьдесят', 'шестьдесят', 'семьдесят', 'восемьдесят', 'девяносто']\r\n"
        + "    hund = ['', 'сто', 'двести', 'триста', 'четыреста', 'пятьсот', 'шестьсот', 'семьсот', 'восемьсот', 'девятьсот']\r\n"
        + "    if n == 0:\r\n"
        + "        return 'ноль'\r\n"
        + "    parts = []\r\n"
        + "    if n >= 100:\r\n"
        + "        parts.append(hund[n // 100])\r\n"
        + "        n %= 100\r\n"
        + "    if n >= 20:\r\n"
        + "        parts.append(tens[n // 10])\r\n"
        + "        if n % 10:\r\n"
        + "            parts.append(ones[n % 10])\r\n"
        + "    elif n > 0:\r\n"
        + "        parts.append(ones[n])\r\n"
        + "    return ' '.join(parts)\r\n"
        + "def ru_big(n):\r\n"
        + "    if n >= 1000000:\r\n"
        + "        m = n // 1000000\r\n"
        + "        tail = '' if n % 1000000 == 0 else ' ' + ru_big(n % 1000000)\r\n"
        + "        mod = m % 100\r\n"
        + "        word = 'миллионов'\r\n"
        + "        if mod < 11 or mod > 14:\r\n"
        + "            if m % 10 == 1: word = 'миллион'\r\n"
        + "            elif 2 <= m % 10 <= 4: word = 'миллиона'\r\n"
        + "        return (ru_chunk(m) + ' ' + word + tail).strip()\r\n"
        + "    if n >= 1000:\r\n"
        + "        t = n // 1000\r\n"
        + "        tail = '' if n % 1000 == 0 else ' ' + ru_chunk(n % 1000)\r\n"
        + "        mod = t % 100\r\n"
        + "        word = 'тысяч'\r\n"
        + "        parts = ru_chunk(t).split(' ')\r\n"
        + "        if parts and parts[-1] == 'один': parts[-1] = 'одна'\r\n"
        + "        elif parts and parts[-1] == 'два': parts[-1] = 'две'\r\n"
        + "        head = ' '.join(parts)\r\n"
        + "        if mod < 11 or mod > 14:\r\n"
        + "            if t % 10 == 1: word = 'тысяча'\r\n"
        + "            elif 2 <= t % 10 <= 4: word = 'тысячи'\r\n"
        + "        return (head + ' ' + word + tail).strip()\r\n"
        + "    return ru_chunk(n)\r\n"
        + "def en_chunk(n):\r\n"
        + "    ones = ['', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen']\r\n"
        + "    tens = ['', '', 'twenty', 'thirty', 'forty', 'fifty', 'sixty', 'seventy', 'eighty', 'ninety']\r\n"
        + "    if n == 0:\r\n"
        + "        return 'zero'\r\n"
        + "    parts = []\r\n"
        + "    if n >= 100:\r\n"
        + "        parts.append(ones[n // 100] + ' hundred')\r\n"
        + "        n %= 100\r\n"
        + "    if n >= 20:\r\n"
        + "        parts.append(tens[n // 10])\r\n"
        + "        if n % 10:\r\n"
        + "            parts.append(ones[n % 10])\r\n"
        + "    elif n > 0:\r\n"
        + "        parts.append(ones[n])\r\n"
        + "    return ' '.join(parts)\r\n"
        + "def en_big(n):\r\n"
        + "    if n >= 1000000:\r\n"
        + "        tail = '' if n % 1000000 == 0 else ' ' + en_big(n % 1000000)\r\n"
        + "        return (en_chunk(n // 1000000) + ' million' + tail).strip()\r\n"
        + "    if n >= 1000:\r\n"
        + "        tail = '' if n % 1000 == 0 else ' ' + en_chunk(n % 1000)\r\n"
        + "        return (en_big(n // 1000) + ' thousand' + tail).strip()\r\n"
        + "    return en_chunk(n)\r\n"
        + "def speak_numbers(text, lang):\r\n"
        + "    if not text:\r\n"
        + "        return ''\r\n"
        + "    text = text.replace('\\u00a0', ' ')\r\n"
        + "    text = re.sub(r'(?<=\\d)[ ](?=\\d{3}\\b)', '', text)\r\n"
        + "    def words(raw, english):\r\n"
        + "        try:\r\n"
        + "            value = int(raw)\r\n"
        + "        except Exception:\r\n"
        + "            return raw\r\n"
        + "        if value < 0 or value > 999999999:\r\n"
        + "            return raw\r\n"
        + "        return en_big(value) if english else ru_big(value)\r\n"
        + "    def repl(match):\r\n"
        + "        raw = match.group(1)\r\n"
        + "        frac = match.group(2)\r\n"
        + "        start = max(0, match.start() - 12)\r\n"
        + "        window = text[start:match.end() + 12]\r\n"
        + "        english = lang == 'en' or (len(re.findall(r'[A-Za-z]', window)) > len(re.findall(r'[А-Яа-яЁё]', window)) and lang != 'ru')\r\n"
        + "        spoken = words(raw, english)\r\n"
        + "        if frac:\r\n"
        + "            spoken = spoken + (' point ' if english else ' целых ') + words(frac, english)\r\n"
        + "        tail = match.group(3) or ''\r\n"
        + "        if tail == '%':\r\n"
        + "            spoken = spoken + (' percent' if english else ' процентов')\r\n"
        + "        elif tail == '₽' or tail == 'р' or tail == 'Р':\r\n"
        + "            spoken = spoken + (' rubles' if english else ' рублей')\r\n"
        + "        elif tail == '$':\r\n"
        + "            spoken = spoken + (' dollars' if english else ' долларов')\r\n"
        + "        elif tail == '€':\r\n"
        + "            spoken = spoken + (' euros' if english else ' евро')\r\n"
        + "        return ' ' + spoken + ' '\r\n"
        + "    text = re.sub(r'(\\d+)(?:[.,](\\d{1,2}))?([%₽$€]|(?:[рР](?![A-Za-zА-Яа-яЁё])))?', repl, text)\r\n"
        + "    return re.sub(r'\\s+', ' ', text).strip()\r\n"
        + "while True:\r\n"
        + "    line = sys.stdin.readline()\r\n"
        + "    if not line:\r\n"
        + "        break\r\n"
        + "    line = line.strip()\r\n"
        + "    if not line:\r\n"
        + "        continue\r\n"
        + "    try:\r\n"
        + "        req = json.loads(line)\r\n"
        + "        speaker = req.get('speaker') or ''\r\n"
        + "        lang = req.get('lang') or 'ru'\r\n"
        + "        text = speak_numbers(req.get('text') or '', lang)\r\n"
        + "        rate = 48000\r\n"
        + "        if lang == 'en' and en is not None:\r\n"
        + "            audio = en.apply_tts(text=text, speaker=speaker or 'en_0', sample_rate=rate)\r\n"
        + "        else:\r\n"
        + "            speaker = speaker or 'xenia'\r\n"
        + "            audio = None\r\n"
        + "            last = None\r\n"
        + "            for extra in ({'put_accent': True, 'put_yo': True}, {'put_accent': True}, {}):\r\n"
        + "                try:\r\n"
        + "                    audio = ru.apply_tts(text=text, speaker=speaker, sample_rate=rate, **extra)\r\n"
        + "                    break\r\n"
        + "                except TypeError as ex:\r\n"
        + "                    last = ex\r\n"
        + "                except Exception as ex:\r\n"
        + "                    last = ex\r\n"
        + "            if audio is None:\r\n"
        + "                raise last\r\n"
        + "        path = os.path.join(root, 'tmp', 'say.wav')\r\n"
        + "        write_wav(path, audio, rate)\r\n"
        + "        real_out.write('OK ' + path + '\\n')\r\n"
        + "        real_out.flush()\r\n"
        + "    except Exception as ex:\r\n"
        + "        real_out.write('ERR ' + str(ex).replace('\\n', ' ') + '\\n')\r\n"
        + "        real_out.flush()\r\n";
    }

    static void EnableSite(string pythonDir)
    {
      string[] files = Directory.GetFiles(pythonDir, "python*._pth");
      if (files.Length == 0) throw new Exception("Python распаковался без файла настройки.");
      string name = Path.GetFileName(files[0]);
      string stem = name.EndsWith("._pth", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 5) : "python311";
      Directory.CreateDirectory(Path.Combine(pythonDir, "Lib", "site-packages"));
      File.WriteAllText(files[0], stem + ".zip\r\n.\r\nLib\\site-packages\r\nimport site\r\n");
    }

    static void Run(string python, string args, string work, Action<string> status)
    {
      ProcessStartInfo info = new ProcessStartInfo();
      info.FileName = python;
      info.Arguments = args;
      info.WorkingDirectory = work;
      info.UseShellExecute = false;
      info.CreateNoWindow = true;
      info.RedirectStandardOutput = true;
      info.RedirectStandardError = true;
      info.StandardOutputEncoding = new UTF8Encoding(false);
      info.StandardErrorEncoding = new UTF8Encoding(false);
      info.EnvironmentVariables["PYTHONUTF8"] = "1";
      info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
      info.EnvironmentVariables["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
      Process process = Process.Start(info);
      string last = "";
      process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
      {
        if (e.Data == null || e.Data.Length == 0) return;
        last = e.Data;
        if (status != null) status(Short(e.Data));
      };
      process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
      {
        if (e.Data == null || e.Data.Length == 0) return;
        last = e.Data;
        if (status != null) status(Short(e.Data));
      };
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();
      if (!process.WaitForExit(40 * 60 * 1000))
      {
        try { process.Kill(true); } catch { }
        throw new Exception("Шаг завис. " + Short(last));
      }
      process.WaitForExit();
      if (process.ExitCode != 0) throw new Exception("Не удалось поставить компонент. " + Short(last));
    }

    static string Short(string line)
    {
      if (line == null) return "";
      line = line.Trim();
      if (line.Length > 140) return line.Substring(0, 140);
      return line;
    }

    public static void Download(string url, string dest, Action<string> status, string title)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(dest));
      if (File.Exists(dest))
      {
        long len = new FileInfo(dest).Length;
        bool model = title == "Модель";
        if (model && len > 100L * 1024L * 1024L) return;
        if (!model && len > 1000) return;
        try { File.Delete(dest); } catch { }
      }
      string part = dest + ".part";
      using (HttpClient client = new HttpClient())
      {
        client.Timeout = TimeSpan.FromMinutes(120);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Net.UserAgent);
        using (HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
        {
          response.EnsureSuccessStatusCode();
          long total = response.Content.Headers.ContentLength ?? -1;
          using (Stream input = response.Content.ReadAsStreamAsync().Result)
          using (FileStream output = File.Create(part))
          {
            byte[] buffer = new byte[1024 * 128];
            long got = 0;
            int read;
            int lastPercent = -1;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
              output.Write(buffer, 0, read);
              got += read;
              if (status != null && total > 0)
              {
                int percent = (int)(got * 100L / total);
                if (percent != lastPercent)
                {
                  lastPercent = percent;
                  status(title + " " + percent.ToString(CultureInfo.InvariantCulture) + "%");
                }
              }
            }
          }
        }
      }
      if (File.Exists(dest)) File.Delete(dest);
      File.Move(part, dest);
    }
  }

  public class Speaker
  {
    public class Cue
    {
      public string Text;
      public string Speaker;
      public bool English;
      public string KeepPath;
      public bool Priority;
    }

    readonly object speakGate = new object();
    readonly Queue<Cue> urgent = new Queue<Cue>();
    readonly Queue<Cue> later = new Queue<Cue>();
    readonly AutoResetEvent speakPulse = new AutoResetEvent(false);
    volatile bool speakRun = true;
    volatile bool hold;
    volatile bool speaking;
    int skipped;
    VoicePrefs prefs = new VoicePrefs();
    Thread thread;
    SpeechSynthesizer synth;

    public bool Speaking
    {
      get { return speaking; }
    }

    public bool Hold
    {
      get { return hold; }
      set
      {
        hold = value;
        speakPulse.Set();
      }
    }

    public int Queued
    {
      get
      {
        lock (speakGate) return urgent.Count + later.Count;
      }
    }

    public int Skipped
    {
      get { return skipped; }
    }

    public string LastError;
    public Action<string> OutputsFound;

    public void Start()
    {
      thread = new Thread(Loop);
      thread.IsBackground = true;
      thread.Name = "RechSpeaker";
      thread.Start();
    }

    public void Apply(VoicePrefs next)
    {
      prefs = next;
    }

    public void Say(string line, bool force)
    {
      Say(line, force, null);
    }

    public void Say(string line, bool force, string speaker)
    {
      Say(line, force, speaker, false);
    }

    public void Say(string line, bool force, string speaker, bool english)
    {
      Say(line, force, speaker, english, null);
    }

    public void Say(string line, bool force, string speaker, bool english, string keepPath)
    {
      Say(line, force, speaker, english, keepPath, false);
    }

    public void Say(string line, bool force, string speaker, bool english, string keepPath, bool priority)
    {
      if (line == null || line.Length == 0) return;
      VoicePrefs current = prefs;
      if (!force && (current == null || !current.Enabled)) return;
      Cue cue = new Cue();
      cue.Text = line;
      cue.Speaker = speaker;
      cue.English = english;
      cue.KeepPath = keepPath;
      cue.Priority = priority;
      lock (speakGate)
      {
        Queue<Cue> bucket = priority ? urgent : later;
        if (bucket.Count >= 8)
        {
          Interlocked.Increment(ref skipped);
          return;
        }
        bucket.Enqueue(cue);
      }
      speakPulse.Set();
    }

    public void ClearQueue()
    {
      lock (speakGate)
      {
        urgent.Clear();
        later.Clear();
      }
    }

    public void Stop()
    {
      speakRun = false;
      speakPulse.Set();
      try
      {
        if (synth != null) synth.SpeakAsyncCancelAll();
      }
      catch
      {
      }
    }

    void Loop()
    {
      while (speakRun)
      {
        Cue cue = null;
        lock (speakGate)
        {
          if (urgent.Count > 0) cue = urgent.Dequeue();
          else if (later.Count > 0) cue = later.Dequeue();
        }
        if (cue == null)
        {
          speakPulse.WaitOne(250);
          continue;
        }
        while (hold && speakRun) Thread.Sleep(40);
        if (!speakRun) break;
        speaking = true;
        try
        {
          SpeakOne(cue);
          LastError = null;
        }
        catch (Exception ex)
        {
          LastError = ex.Message;
        }
        finally
        {
          speaking = false;
        }
      }
    }

    void SpeakOne(Cue cue)
    {
      VoicePrefs current = prefs;
      if (current == null) current = new VoicePrefs();
      string line = cue == null ? "" : cue.Text;
      PlaySilero(line, current, cue == null ? null : cue.Speaker, cue == null ? null : cue.KeepPath);
    }

    void PlaySilero(string line, VoicePrefs current, string speakerOverride, string keepPath)
    {
      string speaker = speakerOverride;
      if (speaker == null || speaker.Length == 0) speaker = current.SileroSpeaker;
      if (speaker == null || speaker.Length == 0) speaker = "xenia";
      if (!SileroLocal.Ready) throw new Exception("Голос ещё не скачан. Нажми «Скачать голос» один раз.");
      byte[] bytes = SileroLocal.SpeakLong(line, speaker);
      string path = Path.Combine(Path.GetTempPath(), "rech-" + Guid.NewGuid().ToString("N") + ".wav");
      File.WriteAllBytes(path, bytes);
      try
      {
        if (bytes.Length < 12 || bytes[0] != (byte)'R' || bytes[1] != (byte)'I')
        {
          throw new Exception("Движок отдал не wav.");
        }
        AdjustWav(path, current.Volume / 100f, current.RatePercent / 100f);
        using (SoundPlayer player = new SoundPlayer(path))
        {
          player.PlaySync();
        }
      }
      finally
      {
        if (keepPath != null && keepPath.Length > 0)
        {
          try
          {
            string dir = Path.GetDirectoryName(keepPath);
            if (dir != null && dir.Length > 0 && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.Copy(path, keepPath, true);
          }
          catch
          {
          }
        }
        try { File.Delete(path); } catch { }
      }
    }

    static string AudioFromSse(string raw, string baseUrl, out string local)
    {
      local = null;
      if (raw == null) return null;
      string[] blocks = raw.Split(new string[] { "\n\n" }, StringSplitOptions.None);
      string found = null;
      foreach (string block in blocks)
      {
        StringBuilder data = new StringBuilder();
        string[] lines = block.Split('\n');
        foreach (string line in lines)
        {
          if (line.StartsWith("data:"))
          {
            string piece = line.Substring(5).Trim();
            data.Append(piece);
          }
        }
        if (data.Length == 0) continue;
        try
        {
          object parsed = Net.Box(data.ToString());
          string url = WalkAudio(parsed, baseUrl);
          string file = FindLocalWav(parsed);
          if (url != null) found = url;
          if (file != null) local = file;
        }
        catch
        {
        }
      }
      return found;
    }

    static string WalkAudio(object node, string baseUrl)
    {
      if (node == null) return null;
      string text = node as string;
      if (text != null)
      {
        if (text.StartsWith("http")) return text;
        if (text.StartsWith("/") && baseUrl != null) return baseUrl + text;
        return null;
      }
      IList list = node as IList;
      if (list != null)
      {
        string found = null;
        foreach (object item in list)
        {
          string next = WalkAudio(item, baseUrl);
          if (next != null) found = next;
        }
        return found;
      }
      Dictionary<string, object> dict = node as Dictionary<string, object>;
      if (dict == null) return null;
      if (dict.ContainsKey("url"))
      {
        string url = WalkAudio(dict["url"], baseUrl);
        if (url != null) return url;
      }
      if (dict.ContainsKey("path"))
      {
        string url = WalkAudio(dict["path"], baseUrl);
        if (url != null) return url;
      }
      if (dict.ContainsKey("data")) return WalkAudio(dict["data"], baseUrl);
      return null;
    }

    static string FindLocalWav(object node)
    {
      string best = null;
      FindLocalWav(node, ref best);
      return best;
    }

    static void FindLocalWav(object node, ref string best)
    {
      if (node == null) return;
      string text = node as string;
      if (text != null)
      {
        Consider(PathFromFileUrl(text), ref best);
        Consider(text, ref best);
        Consider(AbsoluteInText(text), ref best);
        return;
      }
      IList list = node as IList;
      if (list != null)
      {
        foreach (object item in list) FindLocalWav(item, ref best);
        return;
      }
      Dictionary<string, object> dict = node as Dictionary<string, object>;
      if (dict == null) return;
      foreach (object value in dict.Values) FindLocalWav(value, ref best);
    }

    static void Consider(string path, ref string best)
    {
      if (!IsSileroWav(path)) return;
      if (best == null) best = path;
      bool abs = path.IndexOf(':') >= 0 || path.StartsWith("\\\\");
      bool bestAbs = best.IndexOf(':') >= 0 || best.StartsWith("\\\\");
      if (abs && !bestAbs) best = path;
      if (abs && path.IndexOf("outputs", StringComparison.OrdinalIgnoreCase) >= 0) best = path;
    }

    static string PathFromFileUrl(string url)
    {
      if (url == null) return null;
      int mark = url.IndexOf("file=", StringComparison.OrdinalIgnoreCase);
      if (mark < 0) return null;
      string raw = url.Substring(mark + 5);
      int amp = raw.IndexOf('&');
      if (amp >= 0) raw = raw.Substring(0, amp);
      raw = raw.Trim().Trim('"');
      if (raw.Length == 0) return null;
      try
      {
        raw = Uri.UnescapeDataString(raw.Replace("+", "%2B"));
      }
      catch
      {
      }
      return raw;
    }

    static string AbsoluteInText(string raw)
    {
      if (raw == null || raw.Length == 0) return null;
      string text = raw.Replace("\\\\", "\\");
      try
      {
        text = Uri.UnescapeDataString(text);
      }
      catch
      {
      }
      MatchCollection matches = Regex.Matches(text, @"[A-Za-z]:[\\/][^\r\n""'<>|*?]+?\.wav", RegexOptions.IgnoreCase);
      string found = null;
      foreach (Match match in matches)
      {
        string path = match.Value.Replace('/', '\\');
        if (!IsSileroWav(path)) continue;
        found = path;
        if (path.IndexOf("\\outputs\\", StringComparison.OrdinalIgnoreCase) >= 0) return path;
      }
      return found;
    }

    static bool IsSileroWav(string path)
    {
      if (path == null) return false;
      if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
      string name;
      try { name = Path.GetFileName(path); }
      catch { return false; }
      if (name == null) return false;
      return name.StartsWith("silero_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
    }

    void DeleteSileroOutputs(string local, string url, string raw, string configured)
    {
      List<string> dirs = new List<string>();
      AddOutputs(dirs, configured);
      AddOutputs(dirs, DirectoryOf(local));
      AddOutputs(dirs, DirectoryOf(PathFromFileUrl(url)));
      AddOutputs(dirs, DirectoryOf(AbsoluteInText(raw)));
      AddOutputs(dirs, DirectoryOf(AbsoluteInText(local)));
      AddOutputs(dirs, DirectoryOf(AbsoluteInText(url)));
      TryDeleteWav(local);
      TryDeleteWav(PathFromFileUrl(url));
      TryDeleteWav(AbsoluteInText(raw));
      if (dirs.Count == 0) AddOutputs(dirs, DetectOutputsFolder());
      int i;
      for (i = 0; i < dirs.Count; i++)
      {
        SweepOutputs(dirs[i]);
        Action<string> found = OutputsFound;
        if (found != null) found(dirs[i]);
      }
    }

    static void AddOutputs(List<string> dirs, string path)
    {
      string dir = NormalizeOutputs(path);
      if (dir == null) return;
      int i;
      for (i = 0; i < dirs.Count; i++)
      {
        if (string.Equals(dirs[i], dir, StringComparison.OrdinalIgnoreCase)) return;
      }
      dirs.Add(dir);
    }

    static string DirectoryOf(string path)
    {
      if (!IsSileroWav(path)) return null;
      try { return Path.GetDirectoryName(path); }
      catch { return null; }
    }

    static string NormalizeOutputs(string path)
    {
      if (path == null) return null;
      path = path.Trim().Trim('"');
      if (path.Length == 0) return null;
      try
      {
        if (Directory.Exists(Path.Combine(path, "outputs"))) path = Path.Combine(path, "outputs");
        if (!Directory.Exists(path)) return null;
        if (!string.Equals(Path.GetFileName(path.TrimEnd('\\', '/')), "outputs", StringComparison.OrdinalIgnoreCase)) return null;
        return path;
      }
      catch
      {
        return null;
      }
    }

    public static string DetectOutputsFolder()
    {
      string best = null;
      DateTime bestTime = DateTime.MinValue;
      Process[] processes;
      try { processes = Process.GetProcesses(); }
      catch { return null; }
      int p;
      for (p = 0; p < processes.Length; p++)
      {
        string start = null;
        try { start = processes[p].MainModule.FileName; }
        catch { continue; }
        string dir = null;
        try { dir = Path.GetDirectoryName(start); }
        catch { continue; }
        int hop;
        for (hop = 0; hop < 7 && dir != null; hop++)
        {
          string outputs = Path.Combine(dir, "outputs");
          DateTime stamp;
          if (NewestSilero(outputs, out stamp) && stamp >= bestTime)
          {
            bestTime = stamp;
            best = outputs;
          }
          try { dir = Path.GetDirectoryName(dir); }
          catch { break; }
        }
      }
      return best;
    }

    static bool NewestSilero(string dir, out DateTime stamp)
    {
      stamp = DateTime.MinValue;
      try
      {
        if (!Directory.Exists(dir)) return false;
        string[] files = Directory.GetFiles(dir, "silero_*.wav");
        if (files.Length == 0) return false;
        int i;
        for (i = 0; i < files.Length; i++)
        {
          DateTime t = File.GetLastWriteTimeUtc(files[i]);
          if (t > stamp) stamp = t;
        }
        return true;
      }
      catch
      {
        return false;
      }
    }

    public static void SweepOutputs(string dir)
    {
      dir = NormalizeOutputs(dir);
      if (dir == null) return;
      try
      {
        string[] files = Directory.GetFiles(dir, "silero_*.wav");
        int i;
        for (i = 0; i < files.Length; i++) TryDelete(files[i]);
        string parent = Path.GetDirectoryName(dir);
        if (parent != null) TryDelete(Path.Combine(parent, "temp_output.wav"));
      }
      catch
      {
      }
    }

    static void TryDeleteWav(string path)
    {
      if (!IsSileroWav(path)) return;
      try
      {
        if (File.Exists(path)) TryDelete(path);
      }
      catch
      {
      }
    }

    static void TryDelete(string file)
    {
      int n;
      for (n = 0; n < 6; n++)
      {
        try
        {
          if (File.Exists(file)) File.Delete(file);
          return;
        }
        catch
        {
          Thread.Sleep(200);
        }
      }
    }

    static void AdjustWav(string path, float volume, float speed)
    {
      if (volume > 0.99f && volume < 1.01f && speed > 0.99f && speed < 1.01f) return;
      byte[] data = File.ReadAllBytes(path);
      int fmt = IndexOf(data, Encoding.ASCII.GetBytes("fmt "));
      int dataPos = IndexOf(data, Encoding.ASCII.GetBytes("data"));
      if (fmt < 0 || dataPos < 0 || dataPos + 8 > data.Length) return;
      int audioFormat = BitConverter.ToInt16(data, fmt + 8);
      int channels = BitConverter.ToInt16(data, fmt + 10);
      int bits = BitConverter.ToInt16(data, fmt + 22);
      int sampleRate = BitConverter.ToInt32(data, fmt + 12);
      if (channels <= 0) channels = 1;
      if (speed < 0.5f) speed = 0.5f;
      if (speed > 2f) speed = 2f;
      int newRate = (int)Math.Round(sampleRate * speed);
      if (newRate < 8000) newRate = 8000;
      WriteInt(data, fmt + 12, newRate);
      int byteRate = newRate * channels * (bits / 8);
      WriteInt(data, fmt + 16, byteRate);
      int count = BitConverter.ToInt32(data, dataPos + 4);
      int start = dataPos + 8;
      if (count < 0 || start + count > data.Length) count = data.Length - start;
      if (volume < 0f) volume = 0f;
      if (volume > 1f) volume = 1f;
      if (audioFormat == 1 && bits == 16)
      {
        int i;
        for (i = 0; i + 1 < count; i += 2)
        {
          short sample = BitConverter.ToInt16(data, start + i);
          int scaled = (int)Math.Round(sample * volume);
          if (scaled > short.MaxValue) scaled = short.MaxValue;
          if (scaled < short.MinValue) scaled = short.MinValue;
          byte[] next = BitConverter.GetBytes((short)scaled);
          data[start + i] = next[0];
          data[start + i + 1] = next[1];
        }
      }
      else if (audioFormat == 3 && bits == 32)
      {
        int i;
        for (i = 0; i + 3 < count; i += 4)
        {
          float sample = BitConverter.ToSingle(data, start + i) * volume;
          if (sample > 1f) sample = 1f;
          if (sample < -1f) sample = -1f;
          byte[] next = BitConverter.GetBytes(sample);
          data[start + i] = next[0];
          data[start + i + 1] = next[1];
          data[start + i + 2] = next[2];
          data[start + i + 3] = next[3];
        }
      }
      File.WriteAllBytes(path, data);
    }

    static int IndexOf(byte[] hay, byte[] needle)
    {
      int i;
      for (i = 0; i <= hay.Length - needle.Length; i++)
      {
        bool ok = true;
        int j;
        for (j = 0; j < needle.Length; j++)
        {
          if (hay[i + j] != needle[j])
          {
            ok = false;
            break;
          }
        }
        if (ok) return i;
      }
      return -1;
    }

    static void WriteInt(byte[] data, int offset, int value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      data[offset] = bytes[0];
      data[offset + 1] = bytes[1];
      data[offset + 2] = bytes[2];
      data[offset + 3] = bytes[3];
    }
  }

  public class MainForm : Form
  {
    static readonly Color Canvas = Color.FromArgb(16, 14, 12);
    static readonly Color Surface = Color.FromArgb(22, 17, 14);
    static readonly Color Raised = Color.FromArgb(36, 25, 16);
    static readonly Color Ink = Color.FromArgb(246, 239, 228);
    static readonly Color Mute = Color.FromArgb(183, 170, 154);
    static readonly Color Line = Color.FromArgb(58, 48, 40);
    static readonly Color Live = Color.FromArgb(255, 122, 60);
    static readonly Color Gold = Color.FromArgb(231, 196, 138);
    static readonly Color OnLive = Color.FromArgb(26, 13, 7);
    static readonly Color MagentaKey = Color.FromArgb(255, 0, 255);

    readonly FeedEngine engine = new FeedEngine();
    readonly Speaker speaker = new Speaker();
    readonly List<SourceConfig> sources = new List<SourceConfig>();
    readonly HashSet<string> seen = new HashSet<string>();
    readonly List<string> voiceNames = new List<string>();
    readonly string settingsPath;

    Panel topBar;
    Panel left;
    Panel right;
    Panel center;
    ListBox chat;
    Label emptyLabel;
    Button downButton;
    Label stateLabel;
    Font boldFont;
    bool quitting;
    FlowLayoutPanel sourceList;
    ComboBox platformBox;
    TextBox channelBox;
    ComboBox engineBox;
    ComboBox voiceBox;
    ComboBox sileroBox;
    TextBox sileroUrl;
    TextBox sileroOutputs;
    Label sileroState;
    Button sileroInstall;
    bool installing;
    Label rateLabel;
    TrackBar rateBar;
    Label volumeLabel;
    TrackBar volumeBar;
    CheckBox ttsBox;
    CheckBox lucyBox;
    Label lucyState;
    Button lucyInstall;
    TextBox lucyFacts;
    TextBox twitchClient;
    TextBox songRewardBox;
    TextBox lucyRewardBox;
    TextBox songSecondsBox;
    Label songVolumeLabel;
    TrackBar songVolumeBar;
    CheckBox songYtBox;
    CheckBox songRuBox;
    CheckBox songVkBox;
    Label rewardState;
    Label songNow;
    TextBox playerBox;
    string twitchToken = "";
    string twitchRefresh = "";
    string twitchBotToken = "";
    string twitchBotRefresh = "";
    string twitchBotLogin = "";
    Label botState;
    bool lucyBusy;
    DateTime lucyReadyAt;
    CheckBox authorBox;
    CheckBox englishBox;
    CheckBox numberBox;
    CheckBox emoteBox;
    CheckBox swearBox;
    Dictionary<string, string> audienceNow = new Dictionary<string, string>();
    System.Windows.Forms.Timer audienceTimer;
    TextBox muteBox;
    Label errorLabel;
    CheckBox topBox;
    CheckBox platformMarkBox;
    CheckBox timeBox;
    CheckBox rehearsalBox;
    CheckBox chromaBox;
    readonly OverlayHub hub = new OverlayHub();
    ReaderWindow chatWindow;
    Button topChatButton;
    ComboBox fontBox;
    TrackBar fontSizeBar;
    Label fontSizeLabel;
    CheckBox fontBoldBox;
    Button textColorButton;
    Button nickColorButton;
    ComboBox strokeBox;
    CheckBox plateBox;
    ComboBox alignBox;
    TrackBar linesBar;
    Label linesLabel;
    TrackBar holdBar;
    Label holdLabel;
    TextBox chatWBox;
    TextBox chatHBox;
    Label sizeHint;
    TextBox urlBox;
    Button chatWindowButton;
    Button liveButton;
    bool listening;
    Panel modeBar;
    Panel gamePanel;
    ListBox gameList;
    Label gameStatus;
    Button gameHotkeyButton;
    CheckBox gameDeleteBox;
    CheckBox gameTranslateBox;
    CheckBox gameSpeakBox;
    CheckBox gameHotkeyBox;
    CheckBox gameSoftBox;
    CheckBox gamePictureBox;
    Button earButton;
    WasapiLoopbackCapture earCapture;
    Thread earThread;
    BlockingCollection<float[]> earPhrases;
    List<float> earBuf;
    bool earTalk;
    bool earHot;
    bool earOn;
    int earPhraseSec = 5;
    TrackBar earLen;
    Label earLenLabel;
    DateTime earHold;
    int earInRate;
    readonly object earGate = new object();
    int gameMod = 2;
    Keys gameKey = Keys.Oem3;
    bool armHotkey;
    bool picking;
    bool gameOn;
    const int GameHotId = 0xB17;
    const int PauseHotId = 0xB18;
    NumericUpDown delayBox;
    Button pauseButton;
    Button pauseKeyButton;
    CheckBox pauseKeyBox;
    TextBox nickBox;
    bool voicePaused;
    int pauseMod;
    Keys pauseKey = Keys.F8;
    bool armPause;
    string lastTalk = "";
    bool sizing;
    int chatX = 80;
    int chatY = 120;
    int chatW = 720;
    int chatH = 460;
    string readerFont = "Segoe UI";
    int readerSize = 18;
    Color readerText = Color.FromArgb(246, 239, 228);
    Color readerNick = Color.FromArgb(255, 122, 60);
    Color readerBack = Color.FromArgb(17, 14, 12);
    NotifyIcon tray;
    System.Windows.Forms.Timer rehearsalTimer;
    System.Windows.Forms.Timer uiTimer;
    bool loading = true;
    bool stick = true;
    bool capture;
    int rehearsalCursor;
    Label hintChip;
    Image lucyFace;

    static readonly string[] RehearsalPlatform = new string[] { "twitch", "youtube", "vk", "goodgame", "rutube", "steam", "twitch", "youtube" };
    static readonly string[] RehearsalAuthor = new string[] { "northline", "Кира", "поле", "moth", "света", "dock", "юра", "ноль" };
    static readonly string[] RehearsalText = new string[] {
      "пять часов эфира, чат всё ещё здесь",
      "ссылку на стрим кинула, сообщения пошли",
      "вк видео живой, без авторизации",
      "гг подключился по нику",
      "рутуб читает комментарии эфира",
      "стим молчит, пока нет трансляции — и так и пишет",
      "давай, не теряй следующие",
      "озвучь меня тем же голосом"
    };

    public MainForm()
    {
      settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rech", "settings.json");
      Text = "ЛюсиЧат";
      BackColor = Canvas;
      ForeColor = Ink;
      Font = new Font("Segoe UI", 10f);
      boldFont = new Font(Font, FontStyle.Bold);
      MinimumSize = new Size(880, 560);
      Size = new Size(1120, 740);
      StartPosition = FormStartPosition.CenterScreen;
      try
      {
        string exe = Environment.ProcessPath;
        if (exe != null && exe.Length > 0) Icon = Icon.ExtractAssociatedIcon(exe);
      }
      catch
      {
      }
      if (Icon == null) Icon = MakeIcon();
      lucyFace = LoadFace();

      topBar = new Panel();
      topBar.Dock = DockStyle.Top;
      topBar.Height = 74;
      topBar.BackColor = Canvas;
      topBar.Padding = new Padding(16, 8, 12, 8);
      topBar.Paint += delegate(object sender, PaintEventArgs e)
      {
        using (Pen pen = new Pen(Live, 2f)) e.Graphics.DrawLine(pen, 0, topBar.Height - 2, topBar.Width, topBar.Height - 2);
      };
      Panel face = new Panel();
      face.SetBounds(16, 8, 64, 64);
      face.BackColor = Canvas;
      face.Paint += delegate(object sender, PaintEventArgs e)
      {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (lucyFace != null)
        {
          using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
          {
            path.AddEllipse(2, 2, 60, 60);
            e.Graphics.SetClip(path);
            e.Graphics.DrawImage(lucyFace, new Rectangle(2, 2, 60, 60));
            e.Graphics.ResetClip();
          }
        }
        using (Pen ring = new Pen(Gold, 2f)) e.Graphics.DrawEllipse(ring, 2, 2, 59, 59);
      };

      Label title = new Label();
      title.Text = "ЛюсиЧат";
      title.Font = new Font("Georgia", 22f, FontStyle.Bold);
      title.ForeColor = Ink;
      title.AutoSize = true;
      title.Location = new Point(92, 10);
      Label sub = new Label();
      sub.Text = "Люси читает эфир";
      sub.ForeColor = Mute;
      sub.Font = new Font("Georgia", 10f, FontStyle.Italic);
      sub.AutoSize = true;
      sub.Location = new Point(94, 46);
      LinkLabel credit = new LinkLabel();
      credit.Text = "автор · ghostpot.ru";
      credit.AutoSize = true;
      credit.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
      credit.LinkColor = Gold;
      credit.ActiveLinkColor = Live;
      credit.VisitedLinkColor = Gold;
      credit.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
      credit.Location = new Point(248, 48);
      credit.Cursor = Cursors.Hand;
      credit.LinkClicked += delegate
      {
        try
        {
          Process.Start(new ProcessStartInfo("https://ghostpot.ru") { UseShellExecute = true });
        }
        catch
        {
        }
      };
      stateLabel = new Label();
      stateLabel.ForeColor = Mute;
      stateLabel.AutoSize = false;
      stateLabel.TextAlign = ContentAlignment.MiddleRight;
      stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      stateLabel.SetBounds(360, 22, 420, 24);
      Button captureButton = MakeButton("Моё окно", false);
      captureButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      captureButton.SetBounds(820, 18, 140, 36);
      captureButton.Click += delegate { ToggleChatWindow(); };
      topChatButton = captureButton;
      liveButton = MakeButton("Старт", true);
      liveButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
      liveButton.SetBounds(692, 18, 120, 36);
      liveButton.Click += delegate { ToggleListen(); };
      topBar.Controls.Add(face);
      topBar.Controls.Add(title);
      topBar.Controls.Add(sub);
      topBar.Controls.Add(credit);
      topBar.Controls.Add(stateLabel);
      topBar.Controls.Add(liveButton);
      topBar.Controls.Add(captureButton);
      topBar.Resize += delegate
      {
        captureButton.Left = topBar.Width - captureButton.Width - 12;
        liveButton.Left = captureButton.Left - liveButton.Width - 8;
        stateLabel.Width = Math.Max(80, liveButton.Left - stateLabel.Left - 12);
      };

      left = new Panel();
      left.Dock = DockStyle.Left;
      left.Width = 300;
      left.BackColor = Surface;
      left.Padding = new Padding(14);

      Label channels = new Label();
      channels.Text = "КАНАЛЫ";
      channels.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
      channels.ForeColor = Mute;
      channels.AutoSize = true;
      channels.Location = new Point(14, 12);
      platformBox = new ComboBox();
      platformBox.DropDownStyle = ComboBoxStyle.DropDownList;
      platformBox.Items.AddRange(new object[] { "Twitch", "YouTube", "VK Видео", "GoodGame", "Rutube", "w.tv", "Steam", "DonationAlerts", "Boosty" });
      platformBox.SelectedIndex = 0;
      StyleCombo(platformBox);
      platformBox.SetBounds(14, 36, 272, 28);
      channelBox = new TextBox();
      StyleBox(channelBox);
      channelBox.SetBounds(14, 72, 272, 28);
      channelBox.KeyDown += delegate(object sender, KeyEventArgs e)
      {
        if (e.KeyCode == Keys.Enter)
        {
          AddSource();
          e.SuppressKeyPress = true;
        }
      };
      Button add = MakeButton("Подключить", true);
      add.SetBounds(14, 110, 272, 36);
      add.Click += delegate { AddSource(); };
      Label hint = new Label();
      hint.ForeColor = Mute;
      hint.Text = "Несколько площадок в одну ленту. Если связь падает, канал поднимается сам.";
      hint.SetBounds(14, 154, 272, 48);
      sourceList = new FlowLayoutPanel();
      sourceList.FlowDirection = FlowDirection.TopDown;
      sourceList.WrapContents = false;
      sourceList.AutoScroll = true;
      sourceList.BackColor = Surface;
      sourceList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
      sourceList.SetBounds(8, 208, 284, 400);
      left.Resize += delegate { sourceList.Height = Math.Max(80, left.Height - 220); sourceList.Width = left.Width - 16; };
      left.Controls.Add(channels);
      left.Controls.Add(platformBox);
      left.Controls.Add(channelBox);
      left.Controls.Add(add);
      left.Controls.Add(hint);
      left.Controls.Add(sourceList);
      platformBox.SelectedIndexChanged += delegate { channelBox.ForeColor = Ink; };

      right = new Panel();
      right.Dock = DockStyle.Right;
      right.Width = 320;
      right.AutoScroll = true;
      right.BackColor = Surface;
      int y = 12;
      Label voiceTitle = MakeCaption("ГОЛОС");
      Place(right, voiceTitle, 14, ref y, 28);
      Label voiceHint = MakeWrap("Один диктор на всю сессию. Очередь не перебивает сама себя и не меняет голос посреди фразы.");
      Place(right, voiceHint, 14, ref y, 58);
      ttsBox = MakeCheck("Озвучивать новые сообщения");
      Place(right, ttsBox, 14, ref y, 28);
      engineBox = new ComboBox();
      engineBox.Items.AddRange(new object[] { "Голос Windows", "Silero в РЕЧЬ" });
      engineBox.SelectedIndex = 1;
      voiceBox = new ComboBox();
      Place(right, MakeMute("Диктор"), 14, ref y, 18);
      sileroBox = new ComboBox();
      sileroBox.DropDownStyle = ComboBoxStyle.DropDownList;
      sileroBox.Items.AddRange(new object[] { "Ксения", "Ксюша", "Бая", "Айдар", "Евгений" });
      StyleCombo(sileroBox);
      Place(right, sileroBox, 14, ref y, 28);
      sileroState = MakeWrap("Кнопка качает голос в папку silero рядом с программой. Нужен интернет и около 2 ГБ.");
      Place(right, sileroState, 14, ref y, 58);
      sileroInstall = MakeButton("Скачать голос", true);
      sileroInstall.Click += delegate { StartSileroInstall(); };
      Place(right, sileroInstall, 14, ref y, 36);
      lucyBox = MakeCheck("Люси отвечает");
      Place(right, lucyBox, 14, ref y, 28);
      lucyState = MakeWrap("Если написали «Люси» на любой площадке, она отвечает здесь и пишет в чат Twitch от аккаунта Lucy. Видеокарту не трогает.");
      Place(right, lucyState, 14, ref y, 58);
      lucyInstall = MakeButton("Скачать Люси", true);
      lucyInstall.Click += delegate { StartLucyInstall(); };
      Place(right, lucyInstall, 14, ref y, 36);
      Button lucyLogin = MakeButton("Войти как Lucy", true);
      lucyLogin.Click += delegate { Rewards.BeginBotLogin(twitchClient == null ? "" : twitchClient.Text); };
      Place(right, lucyLogin, 14, ref y, 36);
      botState = MakeWrap("В чат Twitch ещё не входит. Нужен отдельный аккаунт Lucy и модератор на твоём канале.");
      Place(right, botState, 14, ref y, 48);
      Place(right, MakeMute("Что Люси знает. Можно править."), 14, ref y, 18);
      lucyFacts = new TextBox();
      StyleBox(lucyFacts);
      lucyFacts.Multiline = true;
      lucyFacts.ScrollBars = ScrollBars.Vertical;
      lucyFacts.Text = LucyLocal.Card;
      Place(right, lucyFacts, 14, ref y, 110);
      Button loreButton = MakeButton("Лор и память", false);
      loreButton.Click += delegate
      {
        if (LoreForm.Open != null && !LoreForm.Open.IsDisposed)
        {
          LoreForm.Open.Activate();
          return;
        }
        LoreForm lore = new LoreForm();
        lore.Show(this);
      };
      Place(right, loreButton, 14, ref y, 36);
      Place(right, MakeCaption("НАГРАДЫ"), 14, ref y, 28);
      Place(right, MakeWrap("На Twitch создай две награды за баллы и включи «зритель вводит текст». Название должно совпасть с полями ниже. Песня не скачивается: в OBS играет официальный плеер. Чужую музыку поставь на дорожку, которая не пишется в запись эфира, иначе Twitch её заглушит."), 14, ref y, 92);
      Place(right, MakeMute("Client ID с dev.twitch.tv"), 14, ref y, 18);
      twitchClient = new TextBox();
      StyleBox(twitchClient);
      Place(right, twitchClient, 14, ref y, 28);
      Button twitchLogin = MakeButton("Войти для наград", true);
      twitchLogin.Click += delegate { Rewards.BeginLogin(twitchClient.Text); };
      Place(right, twitchLogin, 14, ref y, 36);
      rewardState = MakeWrap("Награды ждут вход через Twitch.");
      Place(right, rewardState, 14, ref y, 48);
      Place(right, MakeMute("Награда песни"), 14, ref y, 18);
      songRewardBox = new TextBox();
      StyleBox(songRewardBox);
      songRewardBox.Text = "Заказать песню";
      Place(right, songRewardBox, 14, ref y, 28);
      Place(right, MakeMute("Награда вопроса Люси"), 14, ref y, 18);
      lucyRewardBox = new TextBox();
      StyleBox(lucyRewardBox);
      lucyRewardBox.Text = "Поговорить с Люси";
      Place(right, lucyRewardBox, 14, ref y, 28);
      Place(right, MakeMute("Сколько секунд играет песня, от 20 до 180"), 14, ref y, 18);
      songSecondsBox = new TextBox();
      StyleBox(songSecondsBox);
      songSecondsBox.Text = "75";
      Place(right, songSecondsBox, 14, ref y, 28);
      songVolumeLabel = MakeMute("Музыка 100");
      Place(right, songVolumeLabel, 14, ref y, 18);
      songVolumeBar = new TrackBar();
      songVolumeBar.Minimum = 0;
      songVolumeBar.Maximum = 100;
      songVolumeBar.TickFrequency = 10;
      songVolumeBar.Value = 100;
      songVolumeBar.BackColor = Surface;
      QuietBar(songVolumeBar);
      Place(right, songVolumeBar, 14, ref y, 36);
      songYtBox = MakeCheck("YouTube");
      songYtBox.Checked = true;
      Place(right, songYtBox, 14, ref y, 28);
      songRuBox = MakeCheck("Rutube");
      songRuBox.Checked = true;
      Place(right, songRuBox, 14, ref y, 28);
      songVkBox = MakeCheck("VK Видео");
      songVkBox.Checked = true;
      Place(right, songVkBox, 14, ref y, 28);
      songNow = MakeWrap("Сейчас тишина.");
      Place(right, songNow, 14, ref y, 40);
      Button testSong = MakeButton("Проверить клип", true);
      testSong.Click += delegate
      {
        if (!Rewards.Replay() && songNow != null) songNow.Text = "Сначала закажи одну ссылку. Эфир для проверки не нужен.";
      };
      Place(right, testSong, 14, ref y, 36);
      Button skipSong = MakeButton("Пропустить песню", false);
      skipSong.Click += delegate { Rewards.Skip(); };
      Place(right, skipSong, 14, ref y, 36);
      Button hideSong = MakeButton("Скрыть плеер", false);
      hideSong.Click += delegate
      {
        bool on = !Rewards.Concealed;
        Rewards.Conceal(on);
        hideSong.Text = on ? "Показать плеер" : "Скрыть плеер";
      };
      Place(right, hideSong, 14, ref y, 36);
      Place(right, MakeMute("Когда песни нет, плашка в OBS сама гаснет и игра видна. Источник «Захват окна» оставь на «ЛюсиЧат — клип». Если плашка осталась, в OBS: Инструменты → Настройки вебсокета → включить сервер."), 14, ref y, 72);
      playerBox = new TextBox();
      StyleBox(playerBox);
      playerBox.ReadOnly = true;
      playerBox.Font = new Font("Consolas", 9f);
      Place(right, playerBox, 14, ref y, 28);
      sileroUrl = new TextBox();
      sileroOutputs = new TextBox();
      rateLabel = MakeMute("Скорость 100%");
      Place(right, rateLabel, 14, ref y, 18);
      rateBar = new TrackBar();
      rateBar.Minimum = 80;
      rateBar.Maximum = 130;
      rateBar.TickFrequency = 10;
      rateBar.BackColor = Surface;
      QuietBar(rateBar);
      Place(right, rateBar, 14, ref y, 36);
      volumeLabel = MakeMute("Голос 100");
      Place(right, volumeLabel, 14, ref y, 18);
      volumeBar = new TrackBar();
      volumeBar.Minimum = 0;
      volumeBar.Maximum = 100;
      volumeBar.TickFrequency = 10;
      volumeBar.BackColor = Surface;
      QuietBar(volumeBar);
      Place(right, volumeBar, 14, ref y, 36);
      authorBox = MakeCheck("Называть ник");
      Place(right, authorBox, 14, ref y, 28);
      emoteBox = MakeCheck("Не читать коды смайлов");
      Place(right, emoteBox, 14, ref y, 28);
      swearBox = MakeCheck("Не читать ругань");
      swearBox.Checked = true;
      Place(right, swearBox, 14, ref y, 28);
      englishBox = MakeCheck("Читать английский");
      Place(right, englishBox, 14, ref y, 28);
      numberBox = MakeCheck("Читать цифры");
      Place(right, numberBox, 14, ref y, 28);
      Place(right, MakeMute("Ждать перед речью, секунд"), 14, ref y, 18);
      delayBox = new NumericUpDown();
      delayBox.Minimum = 0;
      delayBox.Maximum = 30;
      delayBox.BackColor = Raised;
      delayBox.ForeColor = Ink;
      delayBox.BorderStyle = BorderStyle.FixedSingle;
      Place(right, delayBox, 14, ref y, 28);
      pauseButton = MakeButton("Пауза голоса", false);
      pauseButton.Click += delegate { TogglePause(); };
      Place(right, pauseButton, 14, ref y, 36);
      pauseKeyBox = MakeCheck("Клавиша паузы");
      pauseKeyBox.Checked = true;
      Place(right, pauseKeyBox, 14, ref y, 28);
      pauseKeyButton = MakeButton("Клавиша: F8", false);
      pauseKeyButton.Click += delegate
      {
        armPause = true;
        armHotkey = false;
        pauseKeyButton.Text = "Нажми клавишу…";
      };
      Place(right, pauseKeyButton, 14, ref y, 36);
      Place(right, MakeMute("Словарь ников: ник = как сказать"), 14, ref y, 18);
      nickBox = new TextBox();
      StyleBox(nickBox);
      nickBox.Multiline = true;
      nickBox.ScrollBars = ScrollBars.Vertical;
      Place(right, nickBox, 14, ref y, 72);
      Place(right, MakeMute("Не показывать и не читать"), 14, ref y, 18);
      muteBox = new TextBox();
      StyleBox(muteBox);
      muteBox.Multiline = true;
      muteBox.ScrollBars = ScrollBars.Vertical;
      Place(right, muteBox, 14, ref y, 72);
      errorLabel = MakeWrap("");
      errorLabel.ForeColor = Live;
      Place(right, errorLabel, 14, ref y, 48);
      Button test = MakeButton("Проверить голос", true);
      test.Click += delegate { speaker.Say("Проверка. Этот голос останется до конца эфира.", true); };
      Place(right, test, 14, ref y, 36);
      Button clearQueue = MakeButton("Очистить очередь", false);
      clearQueue.Click += delegate { speaker.ClearQueue(); };
      Place(right, clearQueue, 14, ref y, 36);
      y += 8;
      Place(right, MakeCaption("ОКНО"), 14, ref y, 28);
      Label obs = MakeWrap("Ссылка только для OBS: источник «Браузер», вставить, затем Refresh. Это другой чат, не моё окно. Размер ставишь в OBS. Сообщение само гаснет.");
      Place(right, obs, 14, ref y, 72);
      urlBox = new TextBox();
      StyleBox(urlBox);
      urlBox.ReadOnly = true;
      urlBox.Font = new Font("Consolas", 9f);
      Place(right, urlBox, 14, ref y, 28);
      Button copyUrl = MakeButton("Копировать ссылку", true);
      copyUrl.Click += delegate
      {
        try
        {
          Clipboard.SetText(urlBox.Text);
          copyUrl.Text = "Скопировано";
        }
        catch
        {
        }
      };
      Place(right, copyUrl, 14, ref y, 36);
      chatWindowButton = MakeButton("Моё окно — читать", false);
      chatWindowButton.Click += delegate { ToggleChatWindow(); };
      Place(right, chatWindowButton, 14, ref y, 36);
      sizeHint = MakeWrap("Моё окно — на компьютере, с прокруткой. В эфир не попадает.");
      Place(right, sizeHint, 14, ref y, 36);
      chatWBox = new TextBox();
      StyleBox(chatWBox);
      chatWBox.Text = "720";
      Place(right, MakeMute("Ширина моего окна"), 14, ref y, 18);
      Place(right, chatWBox, 14, ref y, 28);
      chatHBox = new TextBox();
      StyleBox(chatHBox);
      chatHBox.Text = "460";
      Place(right, MakeMute("Высота моего окна"), 14, ref y, 18);
      Place(right, chatHBox, 14, ref y, 28);
      chromaBox = MakeCheck("Пурпурный фон у студии");
      Place(right, chromaBox, 14, ref y, 28);
      topBox = MakeCheck("Студия поверх других окон");
      Place(right, topBox, 14, ref y, 28);
      platformMarkBox = MakeCheck("Метка площадки");
      Place(right, platformMarkBox, 14, ref y, 28);
      timeBox = MakeCheck("Время");
      Place(right, timeBox, 14, ref y, 28);
      Place(right, MakeCaption("ТЕКСТ В OBS"), 14, ref y, 28);
      fontBox = new ComboBox();
      fontBox.DropDownStyle = ComboBoxStyle.DropDownList;
      fontBox.Items.AddRange(new object[] { "Segoe UI", "Arial", "Verdana", "Tahoma", "Trebuchet MS", "Georgia", "Impact", "Bahnschrift", "Consolas", "Cascadia Mono", "Comic Sans MS", "Times New Roman" });
      StyleCombo(fontBox);
      Place(right, fontBox, 14, ref y, 28);
      fontSizeLabel = MakeMute("Размер 32");
      Place(right, fontSizeLabel, 14, ref y, 18);
      fontSizeBar = new TrackBar();
      fontSizeBar.Minimum = 18;
      fontSizeBar.Maximum = 72;
      fontSizeBar.TickFrequency = 6;
      fontSizeBar.BackColor = Surface;
      QuietBar(fontSizeBar);
      Place(right, fontSizeBar, 14, ref y, 36);
      fontBoldBox = MakeCheck("Жирный");
      Place(right, fontBoldBox, 14, ref y, 28);
      textColorButton = MakeButton("Цвет текста", false);
      textColorButton.Click += delegate { PickColor(textColorButton); };
      Place(right, textColorButton, 14, ref y, 32);
      nickColorButton = MakeButton("Цвет ника", false);
      nickColorButton.Click += delegate { PickColor(nickColorButton); };
      Place(right, nickColorButton, 14, ref y, 32);
      Place(right, MakeMute("Край букв"), 14, ref y, 18);
      strokeBox = new ComboBox();
      strokeBox.DropDownStyle = ComboBoxStyle.DropDownList;
      strokeBox.Items.AddRange(new object[] { "Тень", "Обводка", "Без края" });
      StyleCombo(strokeBox);
      Place(right, strokeBox, 14, ref y, 28);
      plateBox = MakeCheck("Тёмная подложка");
      Place(right, plateBox, 14, ref y, 28);
      Place(right, MakeMute("Где строки"), 14, ref y, 18);
      alignBox = new ComboBox();
      alignBox.DropDownStyle = ComboBoxStyle.DropDownList;
      alignBox.Items.AddRange(new object[] { "Снизу", "Сверху" });
      StyleCombo(alignBox);
      Place(right, alignBox, 14, ref y, 28);
      linesLabel = MakeMute("Строк на экране 8");
      Place(right, linesLabel, 14, ref y, 18);
      linesBar = new TrackBar();
      linesBar.Minimum = 3;
      linesBar.Maximum = 16;
      linesBar.TickFrequency = 1;
      linesBar.BackColor = Surface;
      QuietBar(linesBar);
      Place(right, linesBar, 14, ref y, 36);
      holdLabel = MakeMute("Держать 8 сек");
      Place(right, holdLabel, 14, ref y, 18);
      holdBar = new TrackBar();
      holdBar.Minimum = 2;
      holdBar.Maximum = 20;
      holdBar.TickFrequency = 1;
      holdBar.BackColor = Surface;
      QuietBar(holdBar);
      Place(right, holdBar, 14, ref y, 36);
      rehearsalBox = MakeCheck("Учебные фразы");
      Place(right, rehearsalBox, 14, ref y, 28);
      Button clearChat = MakeButton("Очистить ленту", false);
      clearChat.Click += delegate
      {
        chat.Items.Clear();
        seen.Clear();
        emptyLabel.Visible = true;
        SyncHub();
      };
      Place(right, clearChat, 14, ref y, 36);
      right.AutoScrollMinSize = new Size(0, y + 24);

      center = new Panel();
      center.Dock = DockStyle.Fill;
      center.BackColor = Canvas;
      chat = new ListBox();
      chat.Dock = DockStyle.Fill;
      chat.IntegralHeight = false;
      chat.DrawMode = DrawMode.OwnerDrawFixed;
      chat.ItemHeight = 58;
      chat.BorderStyle = BorderStyle.None;
      chat.BackColor = Canvas;
      chat.ForeColor = Ink;
      chat.DrawItem += DrawRow;
      EmoteArt.Changed += delegate
      {
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(delegate
        {
          if (chat != null) chat.Invalidate();
        }));
      };
      chat.MouseWheel += delegate { UpdateStick(); };
      chat.MouseDown += delegate(object sender, MouseEventArgs e)
      {
        if (!capture) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
      };
      emptyLabel = new Label();
      emptyLabel.TextAlign = ContentAlignment.TopLeft;
      emptyLabel.ForeColor = Mute;
      emptyLabel.BackColor = Canvas;
      emptyLabel.Text = "Каналов нет. Слева выберите площадку, впишите ник и нажмите «Подключить». Само ничего не пишет и не говорит.";
      emptyLabel.SetBounds(16, 56, 480, 72);
      emptyLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
      downButton = MakeButton("Новые", true);
      downButton.SetBounds(0, 0, 110, 36);
      downButton.Anchor = AnchorStyles.Bottom;
      downButton.Visible = false;
      downButton.Click += delegate
      {
        stick = true;
        if (chat.Items.Count > 0) chat.TopIndex = chat.Items.Count - 1;
        downButton.Visible = false;
      };
      hintChip = new Label();
      hintChip.Text = "  Esc — студия  ";
      hintChip.AutoSize = true;
      hintChip.BackColor = Surface;
      hintChip.ForeColor = Mute;
      hintChip.Visible = false;
      hintChip.Location = new Point(8, 8);
      center.Controls.Add(hintChip);
      center.Controls.Add(downButton);
      center.Controls.Add(emptyLabel);
      center.Controls.Add(chat);
      center.Resize += delegate
      {
        downButton.Left = Math.Max(8, (center.Width - downButton.Width) / 2);
        downButton.Top = center.Height - 48;
      };
      BuildGame();

      Controls.Add(center);
      Controls.Add(left);
      Controls.Add(right);
      Controls.Add(topBar);

      tray = new NotifyIcon();
      tray.Icon = Icon;
      tray.Visible = true;
      tray.Text = "ЛюсиЧат";
      ContextMenuStrip menu = new ContextMenuStrip();
      ToolStripMenuItem liveItem = new ToolStripMenuItem("Старт");
      liveItem.Name = "live";
      liveItem.Click += delegate { ToggleListen(); };
      ToolStripMenuItem openItem = new ToolStripMenuItem("Открыть студию");
      openItem.Click += delegate { RestoreStudio(); };
      ToolStripMenuItem exitItem = new ToolStripMenuItem("Выйти");
      exitItem.Click += delegate { Shutdown(); };
      menu.Items.Add(liveItem);
      menu.Items.Add(openItem);
      menu.Items.Add(exitItem);
      tray.ContextMenuStrip = menu;
      tray.DoubleClick += delegate { RestoreStudio(); };

      engine.Status += delegate(string id, string state, string detail)
      {
        Ui(delegate { UpdateStatus(id, state, detail); });
      };
      engine.Message += delegate(ChatMessage message)
      {
        Ui(delegate { AddMessage(message); });
      };

      LoadVoices();
      LoadSettings();
      Wire();
      loading = false;
      Rewards.Line += OnRewardLine;
      Rewards.TokenReady += delegate(string access, string refresh)
      {
        if (IsDisposed) return;
        BeginInvoke(new Action(delegate
        {
          twitchToken = access == null ? "" : access;
          twitchRefresh = refresh == null ? "" : refresh;
          SaveSettings();
        }));
      };
      Rewards.BotReady += delegate(string access, string refresh, string login)
      {
        if (IsDisposed) return;
        BeginInvoke(new Action(delegate
        {
          twitchBotToken = access == null ? "" : access;
          twitchBotRefresh = refresh == null ? "" : refresh;
          if (login != null && login.Length > 0) twitchBotLogin = login;
          SaveSettings();
        }));
      };
      Rewards.Apply(RewardPrefsNow());
      ApplyVoice();
      ShowSileroReady();
      ShowLucyReady();
      audienceTimer = new System.Windows.Forms.Timer();
      audienceTimer.Interval = 20000;
      audienceTimer.Tick += delegate { PollAudience(); };
      speaker.OutputsFound = RememberOutputs;
      speaker.Start();
      PushStyle();
      hub.Start();
      urlBox.Text = hub.Port > 0 ? "http://127.0.0.1:" + hub.Port.ToString(CultureInfo.InvariantCulture) + "/" : "ссылка не открылась";
      if (playerBox != null) playerBox.Text = hub.Port > 0 ? "http://127.0.0.1:" + hub.Port.ToString(CultureInfo.InvariantCulture) + "/player" : "";
      ClipWindow.Home = playerBox == null ? "" : playerBox.Text;
      ApplyListen();
      UpdatePlaceholder();

      rehearsalTimer = new System.Windows.Forms.Timer();
      rehearsalTimer.Interval = 4500;
      rehearsalTimer.Tick += delegate
      {
        if (!rehearsalBox.Checked) return;
        int index = rehearsalCursor % RehearsalText.Length;
        rehearsalCursor = rehearsalCursor + 1;
        ChatMessage message = new ChatMessage();
        message.Id = "rehearsal:" + Guid.NewGuid().ToString("N");
        message.SourceId = "rehearsal";
        message.Platform = RehearsalPlatform[index];
        message.Author = RehearsalAuthor[index];
        message.Text = RehearsalText[index];
        message.Ts = Net.Now();
        message.Fresh = true;
        AddMessage(message);
      };
      rehearsalTimer.Start();

      uiTimer = new System.Windows.Forms.Timer();
      uiTimer.Interval = 300;
      uiTimer.Tick += delegate
      {
        Rewards.Tick();
        RefreshState();
      };
      uiTimer.Start();
    }

    void Wire()
    {
      EventHandler save = delegate
      {
        if (loading) return;
        rateLabel.Text = "Скорость " + rateBar.Value.ToString(CultureInfo.InvariantCulture) + "%";
        volumeLabel.Text = "Голос " + volumeBar.Value.ToString(CultureInfo.InvariantCulture);
        TopMost = topBox.Checked || capture;
        ApplyVoice();
        SaveSettings();
        chat.Invalidate();
      };
      ttsBox.CheckedChanged += save;
      engineBox.SelectedIndexChanged += save;
      voiceBox.SelectedIndexChanged += save;
      sileroBox.SelectedIndexChanged += save;
      sileroUrl.TextChanged += save;
      sileroOutputs.TextChanged += save;
      rateBar.Scroll += save;
      volumeBar.Scroll += save;
      authorBox.CheckedChanged += save;
      englishBox.CheckedChanged += save;
      numberBox.CheckedChanged += save;
      emoteBox.CheckedChanged += save;
      lucyBox.CheckedChanged += delegate
      {
        if (!loading && lucyBox.Checked && !LucyLocal.Ready) StartLucyInstall();
        save(null, EventArgs.Empty);
      };
      swearBox.CheckedChanged += save;
      delayBox.ValueChanged += save;
      pauseKeyBox.CheckedChanged += delegate
      {
        if (loading) return;
        BindPauseKey();
        SaveSettings();
      };
      nickBox.TextChanged += save;
      if (lucyFacts != null) lucyFacts.TextChanged += save;
      if (twitchClient != null) twitchClient.TextChanged += save;
      if (songRewardBox != null) songRewardBox.TextChanged += save;
      if (lucyRewardBox != null) lucyRewardBox.TextChanged += save;
      if (songSecondsBox != null) songSecondsBox.TextChanged += save;
      if (songVolumeBar != null) songVolumeBar.Scroll += delegate
      {
        if (songVolumeLabel != null) songVolumeLabel.Text = "Музыка " + songVolumeBar.Value.ToString(CultureInfo.InvariantCulture);
        save(null, EventArgs.Empty);
      };
      if (songYtBox != null) songYtBox.CheckedChanged += save;
      if (songRuBox != null) songRuBox.CheckedChanged += save;
      if (songVkBox != null) songVkBox.CheckedChanged += save;
      muteBox.TextChanged += delegate
      {
        if (loading) return;
        RebuildVisible();
        SaveSettings();
      };
      topBox.CheckedChanged += save;
      platformMarkBox.CheckedChanged += save;
      timeBox.CheckedChanged += save;
      chromaBox.CheckedChanged += delegate
      {
        if (loading) return;
        if (capture) ApplyCaptureColors();
        SaveSettings();
      };
      rehearsalBox.CheckedChanged += delegate
      {
        if (loading) return;
        SaveSettings();
      };
      EventHandler look = delegate
      {
        if (loading) return;
        fontSizeLabel.Text = "Размер " + fontSizeBar.Value.ToString(CultureInfo.InvariantCulture);
        linesLabel.Text = "Строк на экране " + linesBar.Value.ToString(CultureInfo.InvariantCulture);
        holdLabel.Text = "Держать " + holdBar.Value.ToString(CultureInfo.InvariantCulture) + " сек";
        PushStyle();
        SaveSettings();
      };
      fontBox.SelectedIndexChanged += look;
      fontSizeBar.Scroll += look;
      fontBoldBox.CheckedChanged += look;
      strokeBox.SelectedIndexChanged += look;
      plateBox.CheckedChanged += look;
      alignBox.SelectedIndexChanged += look;
      linesBar.Scroll += look;
      holdBar.Scroll += look;
      platformMarkBox.CheckedChanged += look;
      timeBox.CheckedChanged += look;
      EventHandler sizeApply = delegate
      {
        if (loading || sizing) return;
        ApplyTypedSize();
      };
      chatWBox.Leave += sizeApply;
      chatHBox.Leave += sizeApply;
      KeyEventHandler sizeKey = delegate(object sender, KeyEventArgs e)
      {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        ApplyTypedSize();
      };
      chatWBox.KeyDown += sizeKey;
      chatHBox.KeyDown += sizeKey;
      platformBox.SelectedIndexChanged += delegate { UpdatePlaceholder(); };
    }

    void LoadVoices()
    {
      voiceNames.Clear();
      voiceBox.Items.Clear();
      try
      {
        using (SpeechSynthesizer probe = new SpeechSynthesizer())
        {
          foreach (InstalledVoice voice in probe.GetInstalledVoices())
          {
            if (!voice.Enabled) continue;
            VoiceInfo info = voice.VoiceInfo;
            voiceNames.Add(info.Name);
            voiceBox.Items.Add(info.Name + " · " + info.Culture.Name);
          }
        }
      }
      catch
      {
      }
      if (voiceBox.Items.Count == 0)
      {
        voiceNames.Add("");
        voiceBox.Items.Add("Голосов Windows нет");
      }
    }

    void LoadSettings()
    {
      Settings settings = null;
      try
      {
        if (File.Exists(settingsPath))
        {
          string raw = File.ReadAllText(settingsPath, Encoding.UTF8);
          settings = Net.Read<Settings>(raw);
        }
      }
      catch
      {
        settings = null;
      }
      if (settings == null) settings = new Settings();
      if (settings.Sources != null)
      {
        foreach (SourceConfig source in settings.Sources)
        {
          if (source != null && source.Id != null && source.Platform != null && source.Input != null) sources.Add(source);
        }
      }
      ttsBox.Checked = settings.Engine == null ? true : settings.TtsEnabled || settings.Engine == null;
      if (settings.Engine == null) ttsBox.Checked = true;
      else ttsBox.Checked = settings.TtsEnabled;
      engineBox.SelectedIndex = 1;
      authorBox.Checked = settings.Engine == null ? true : settings.ReadAuthor;
      if (settings.VoiceName == null && settings.Engine == null) authorBox.Checked = true;
      emoteBox.Checked = settings.Engine == null ? true : settings.SkipEmotes;
      if (lucyBox != null) lucyBox.Checked = settings.LucyOn ?? false;
      if (lucyFacts != null)
      {
        string saved = settings.LucyFacts;
        bool oldCard = saved == null || saved.Trim().Length == 0 || saved.IndexOf("Она рысь", StringComparison.Ordinal) >= 0 || saved.IndexOf("КТО", StringComparison.Ordinal) < 0;
        lucyFacts.Text = oldCard ? LucyLocal.Card : saved;
      }
      if (twitchClient != null) twitchClient.Text = settings.TwitchClientId == null ? "" : settings.TwitchClientId;
      twitchToken = settings.TwitchToken == null ? "" : settings.TwitchToken;
      twitchRefresh = settings.TwitchRefresh == null ? "" : settings.TwitchRefresh;
      twitchBotToken = settings.TwitchBotToken == null ? "" : settings.TwitchBotToken;
      twitchBotRefresh = settings.TwitchBotRefresh == null ? "" : settings.TwitchBotRefresh;
      twitchBotLogin = settings.TwitchBotLogin == null ? "" : settings.TwitchBotLogin;
      if (songRewardBox != null) songRewardBox.Text = settings.SongReward == null || settings.SongReward.Length == 0 ? "Заказать песню" : settings.SongReward;
      if (lucyRewardBox != null) lucyRewardBox.Text = settings.LucyReward == null || settings.LucyReward.Length == 0 ? "Поговорить с Люси" : settings.LucyReward;
      if (songSecondsBox != null) songSecondsBox.Text = settings.SongSeconds < 20 ? "75" : settings.SongSeconds.ToString(CultureInfo.InvariantCulture);
      if (songVolumeBar != null)
      {
        songVolumeBar.Value = settings.SongVolume.HasValue ? Clamp(settings.SongVolume.Value, 0, 100) : 100;
        if (songVolumeLabel != null) songVolumeLabel.Text = "Музыка " + songVolumeBar.Value.ToString(CultureInfo.InvariantCulture);
      }
      if (songYtBox != null) songYtBox.Checked = settings.SongYoutube ?? true;
      if (songRuBox != null) songRuBox.Checked = settings.SongRutube ?? true;
      if (songVkBox != null) songVkBox.Checked = settings.SongVk ?? true;
      if (swearBox != null) swearBox.Checked = settings.HideSwear ?? true;
      englishBox.Checked = settings.ReadEnglish ?? true;
      numberBox.Checked = settings.ReadNumbers ?? true;
      ApplyGameSettings(settings);
      if (settings.Engine == null)
      {
        authorBox.Checked = true;
        emoteBox.Checked = true;
        rehearsalBox.Checked = false;
        platformMarkBox.Checked = true;
        timeBox.Checked = true;
      }
      else
      {
        rehearsalBox.Checked = settings.Version >= 2 && settings.Rehearsal;
        platformMarkBox.Checked = settings.ShowPlatform;
        timeBox.Checked = settings.ShowTime;
      }
      volumeBar.Value = settings.Engine == null ? 100 : Clamp(settings.Volume, 0, 100);
      rateBar.Value = settings.Engine == null || settings.RatePercent < 80 ? 100 : Clamp(settings.RatePercent, 80, 130);
      rateLabel.Text = "Скорость " + rateBar.Value.ToString(CultureInfo.InvariantCulture) + "%";
      volumeLabel.Text = "Голос " + volumeBar.Value.ToString(CultureInfo.InvariantCulture);
      sileroUrl.Text = settings.SileroUrl == null || settings.SileroUrl.Length == 0 ? "http://127.0.0.1:8891" : settings.SileroUrl;
      sileroOutputs.Text = settings.SileroOutputs == null ? "" : settings.SileroOutputs;
      sileroBox.SelectedIndex = SileroIndex(settings.SileroSpeaker);
      muteBox.Text = settings.MuteUsers == null ? "" : settings.MuteUsers;
      if (delayBox != null) delayBox.Value = Math.Max(0, Math.Min(30, settings.SpeechDelay));
      if (nickBox != null) nickBox.Text = settings.NickMap == null ? "" : settings.NickMap;
      if (pauseKeyBox != null) pauseKeyBox.Checked = settings.PauseHotkeyOn ?? true;
      if (settings.PauseHotkey != null)
      {
        string[] pair = settings.PauseHotkey.Split(':');
        int mod;
        int key;
        if (pair.Length == 2 && int.TryParse(pair[0], out mod) && int.TryParse(pair[1], out key))
        {
          pauseMod = mod;
          pauseKey = (Keys)key;
        }
      }
      topBox.Checked = settings.AlwaysOnTop;
      chromaBox.Checked = settings.Chroma;
      TopMost = settings.AlwaysOnTop;
      ApplyOverlaySettings(settings);
      SelectVoice(settings.VoiceName);
      if (settings.WinW >= 880 && settings.WinH >= 520)
      {
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(settings.WinX, settings.WinY, settings.WinW, settings.WinH);
      }
      RebuildSourceList();
    }

    void SelectVoice(string saved)
    {
      int index = 0;
      int i;
      for (i = 0; i < voiceNames.Count; i++)
      {
        string name = voiceNames[i];
        if (saved != null && saved.Length > 0 && name == saved) index = i;
      }
      if ((saved == null || saved.Length == 0))
      {
        for (i = 0; i < voiceBox.Items.Count; i++)
        {
          string label = Convert.ToString(voiceBox.Items[i]);
          if (label != null && (label.IndexOf("ru-RU", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf("Russian", StringComparison.OrdinalIgnoreCase) >= 0))
          {
            index = i;
            break;
          }
        }
      }
      if (voiceBox.Items.Count > 0) voiceBox.SelectedIndex = Math.Min(index, voiceBox.Items.Count - 1);
    }

    static int SileroIndex(string speaker)
    {
      if (speaker == "kseniya") return 1;
      if (speaker == "baya") return 2;
      if (speaker == "aidar") return 3;
      if (speaker == "eugene") return 4;
      return 0;
    }

    static string SileroSpeaker(int index)
    {
      if (index == 1) return "kseniya";
      if (index == 2) return "baya";
      if (index == 3) return "aidar";
      if (index == 4) return "eugene";
      return "xenia";
    }

    static int Clamp(int value, int min, int max)
    {
      if (value < min) return min;
      if (value > max) return max;
      return value;
    }

    void ApplyVoice()
    {
      VoicePrefs prefs = new VoicePrefs();
      prefs.Enabled = ttsBox.Checked;
      prefs.Engine = "silero";
      prefs.VoiceName = voiceBox.SelectedIndex >= 0 && voiceBox.SelectedIndex < voiceNames.Count ? voiceNames[voiceBox.SelectedIndex] : "";
      prefs.SileroUrl = sileroUrl.Text.Trim();
      prefs.SileroOutputs = sileroOutputs.Text.Trim();
      prefs.SileroSpeaker = SileroSpeaker(sileroBox.SelectedIndex < 0 ? 0 : sileroBox.SelectedIndex);
      prefs.Volume = volumeBar.Value;
      prefs.RatePercent = rateBar.Value;
      prefs.ReadAuthor = authorBox.Checked;
      prefs.SkipEmotes = emoteBox.Checked;
      speaker.Apply(prefs);
      sileroBox.Enabled = true;
    }

    void StartSileroInstall()
    {
      if (installing) return;
      installing = true;
      sileroInstall.Enabled = false;
      sileroState.Text = "Начинаю загрузку…";
      Thread worker = new Thread(new ThreadStart(delegate
      {
        try
        {
          SileroLocal.Install(delegate(string status)
          {
            if (IsDisposed) return;
            BeginInvoke(new Action(delegate
            {
              if (sileroState != null) sileroState.Text = status;
            }));
          });
          BeginInvoke(new Action(delegate
          {
            installing = false;
            ShowSileroReady();
          }));
        }
        catch (Exception ex)
        {
          BeginInvoke(new Action(delegate
          {
            installing = false;
            sileroInstall.Enabled = true;
            sileroState.Text = ex.Message;
          }));
        }
      }));
      worker.IsBackground = true;
      worker.Name = "RechSileroInstall";
      worker.Start();
    }

    void ShowSileroReady()
    {
      if (!SileroLocal.Ready)
      {
        sileroState.Text = "Кнопка качает голос в папку silero рядом с программой. Нужен интернет и около 2 ГБ.";
        sileroInstall.Text = "Скачать голос";
        sileroInstall.Enabled = true;
        return;
      }
      sileroState.Text = "Русский голос уже есть. Кнопка докачает английскую модель Silero, голос Windows не используется.";
      sileroInstall.Text = "Скачать английский";
      sileroInstall.Enabled = true;
      if (SileroLocal.EnglishReady)
      {
        sileroState.Text = "Русский и английский голоса внутри РЕЧЬ. Своя фраза читается своим языком.";
        sileroInstall.Text = "Голоса скачаны";
        sileroInstall.Enabled = false;
      }
    }

    void RememberOutputs(string dir)
    {
      if (dir == null || dir.Length == 0) return;
      if (InvokeRequired)
      {
        BeginInvoke(new Action<string>(RememberOutputs), dir);
        return;
      }
      string now = sileroOutputs.Text.Trim();
      if (now.Length > 0) return;
      sileroOutputs.Text = dir;
    }

    void CleanSileroNow()
    {
      string dir = sileroOutputs.Text.Trim();
      if (dir.Length > 0)
      {
        Speaker.SweepOutputs(dir);
        return;
      }
      Thread worker = new Thread(new ThreadStart(delegate
      {
        string found = Speaker.DetectOutputsFolder();
        if (found == null) return;
        Speaker.SweepOutputs(found);
        RememberOutputs(found);
      }));
      worker.IsBackground = true;
      worker.Name = "RechSileroClean";
      worker.Start();
    }

    void AddSource()
    {
      string input = channelBox.Text.Trim();
      if (input.Length == 0) return;
      string platform = PlatformId(platformBox.SelectedIndex);
      foreach (SourceConfig source in sources)
      {
        if (source.Platform == platform && string.Equals(source.Input, input, StringComparison.OrdinalIgnoreCase)) return;
      }
      SourceConfig next = new SourceConfig();
      next.Id = Guid.NewGuid().ToString("N").Substring(0, 10);
      next.Platform = platform;
      next.Input = input;
      next.Enabled = true;
      sources.Add(next);
      channelBox.Text = "";
      RebuildSourceList();
      SaveSettings();
      RefreshFeed();
    }

    void RebuildSourceList()
    {
      sourceList.Controls.Clear();
      if (sources.Count == 0)
      {
        Label empty = new Label();
        empty.Text = "Пока пусто. Репетиция показывает, как ляжет окно.";
        empty.ForeColor = Mute;
        empty.Width = 260;
        empty.Height = 48;
        sourceList.Controls.Add(empty);
        return;
      }
      foreach (SourceConfig source in sources)
      {
        Panel card = new Panel();
        card.Width = 260;
        card.Height = 128;
        card.BackColor = Raised;
        card.Margin = new Padding(6, 0, 6, 8);
        card.Tag = source.Id;
        card.Paint += delegate(object sender, PaintEventArgs e)
        {
          Panel panel = (Panel)sender;
          using (Pen pen = new Pen(Line)) e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
          using (Pen ember = new Pen(Live, 2f)) e.Graphics.DrawLine(ember, 0, 0, 0, panel.Height);
        };
        CheckBox enabled = new CheckBox();
        enabled.Checked = source.Enabled;
        enabled.SetBounds(8, 8, 24, 24);
        enabled.Tag = source.Id;
        enabled.CheckedChanged += delegate(object sender, EventArgs e)
        {
          if (loading) return;
          CheckBox box = (CheckBox)sender;
          string id = (string)box.Tag;
          foreach (SourceConfig item in sources)
          {
            if (item.Id == id) item.Enabled = box.Checked;
          }
          SaveSettings();
          RefreshFeed();
        };
        Label title = new Label();
        title.Text = PlatformLabel(source.Platform) + "  " + source.Input;
        title.ForeColor = Ink;
        title.SetBounds(62, 6, 154, 18);
        ComboBox voice = new ComboBox();
        voice.DropDownStyle = ComboBoxStyle.DropDownList;
        voice.Items.AddRange(new object[] { "Голос из настроек", "Ксения", "Ксюша", "Бая", "Айдар", "Евгений", "По полу ника" });
        StyleCombo(voice);
        voice.Tag = source.Id;
        voice.SetBounds(36, 26, 176, 24);
        voice.SelectedIndex = VoiceIndex(source.Voice);
        voice.SelectedIndexChanged += delegate(object sender, EventArgs e)
        {
          if (loading) return;
          ComboBox box = (ComboBox)sender;
          string id = (string)box.Tag;
          foreach (SourceConfig item in sources)
          {
            if (item.Id != id) continue;
            int index = box.SelectedIndex;
            string chosen = "";
            if (index == 6) chosen = "gender";
            else if (index > 0) chosen = SileroSpeaker(index - 1);
            item.Voice = chosen;
          }
          SaveSettings();
        };
        Label status = new Label();
        status.Name = "status";
        status.Text = !listening ? "на стопе" : (source.Enabled ? "жду связь" : "на паузе");
        status.ForeColor = Mute;
        status.SetBounds(36, 54, 180, 32);
        Label audience = new Label();
        audience.Name = "audience";
        audience.Text = "зрители…";
        audience.ForeColor = Mute;
        audience.SetBounds(36, 88, 200, 32);
        Panel badge = new Panel();
        badge.SetBounds(36, 4, 22, 22);
        badge.Tag = source.Platform;
        badge.BackColor = Raised;
        badge.Paint += delegate(object sender, PaintEventArgs e)
        {
          Panel panel = (Panel)sender;
          DrawBadge(e.Graphics, new Rectangle(0, 0, 22, 22), Convert.ToString(panel.Tag), panel.BackColor);
        };
        Button remove = new Button();
        remove.Text = "×";
        remove.FlatStyle = FlatStyle.Flat;
        remove.FlatAppearance.BorderSize = 0;
        remove.BackColor = Raised;
        remove.ForeColor = Mute;
        remove.SetBounds(220, 8, 32, 32);
        remove.Tag = source.Id;
        remove.Click += delegate(object sender, EventArgs e)
        {
          string id = (string)((Button)sender).Tag;
          sources.RemoveAll(delegate(SourceConfig item) { return item.Id == id; });
          RebuildSourceList();
          SaveSettings();
          RefreshFeed();
        };
        card.Controls.Add(enabled);
        card.Controls.Add(title);
        card.Controls.Add(badge);
        card.Controls.Add(voice);
        card.Controls.Add(status);
        card.Controls.Add(audience);
        card.Controls.Add(remove);
        sourceList.Controls.Add(card);
      }
    }

    void PollAudience()
    {
      if (!listening) return;
      List<SourceConfig> copy = new List<SourceConfig>();
      foreach (SourceConfig source in sources)
      {
        if (!source.Enabled) continue;
        SourceConfig item = new SourceConfig();
        item.Id = source.Id;
        item.Platform = source.Platform;
        item.Input = source.Input;
        copy.Add(item);
      }
      Thread worker = new Thread(new ThreadStart(delegate
      {
        foreach (SourceConfig source in copy)
        {
          string text = AudienceLine(source);
          if (IsDisposed) return;
          BeginInvoke(new Action(delegate { SetAudience(source.Id, text); }));
        }
      }));
      worker.IsBackground = true;
      worker.Name = "RechAudience";
      worker.Start();
    }

    void SetAudience(string id, string text)
    {
      foreach (Control control in sourceList.Controls)
      {
        if (!string.Equals(Convert.ToString(control.Tag), id, StringComparison.Ordinal)) continue;
        foreach (Control child in control.Controls)
        {
          if (child.Name != "audience") continue;
          child.Text = text;
          child.ForeColor = text.StartsWith("в эфире") ? Color.FromArgb(90, 190, 110) : Mute;
        }
      }
      audienceNow[id] = text;
    }

    public List<CrowdBit> CrowdNow()
    {
      List<CrowdBit> list = new List<CrowdBit>();
      foreach (SourceConfig source in sources)
      {
        if (!source.Enabled) continue;
        CrowdBit bit = new CrowdBit();
        bit.Platform = source.Platform;
        bit.Title = PlatformLabel(source.Platform);
        string text = null;
        audienceNow.TryGetValue(source.Id, out text);
        bit.Live = text != null && text.StartsWith("в эфире");
        int count;
        string digits = text == null ? "" : Regex.Match(text, "\\d+").Value;
        if (digits.Length > 0 && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out count)) bit.Count = count;
        else bit.Count = -1;
        list.Add(bit);
      }
      return list;
    }

    static string AudienceLine(SourceConfig source)
    {
      try
      {
        if (source.Platform == "twitch") return TwitchAudience(source.Input);
        if (source.Platform == "youtube") return YoutubeAudience(source.Input);
        if (source.Platform == "vk") return VkAudience(source.Input);
        if (source.Platform == "goodgame") return GoodgameAudience(source.Input);
        if (source.Platform == "rutube") return RutubeAudience(source.Input);
      }
      catch
      {
      }
      return "нет цифры зрителей";
    }

    static string TwitchAudience(string input)
    {
      string channel = input.Trim().ToLowerInvariant();
      channel = Regex.Replace(channel, "^https?://(www\\.)?twitch\\.tv/", "");
      int slash = channel.IndexOf('/');
      if (slash >= 0) channel = channel.Substring(0, slash);
      if (channel.StartsWith("#")) channel = channel.Substring(1);
      string body = "{\"query\":\"{user(login:\\\"" + channel + "\\\"){stream{viewersCount}}}\"}";
      using (HttpClient client = new HttpClient())
      {
        client.Timeout = TimeSpan.FromSeconds(12);
        HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql");
        req.Headers.TryAddWithoutValidation("Client-Id", "kimne78kx3ncx6brgo4mv6wki5h1ko");
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        string raw = client.SendAsync(req).Result.Content.ReadAsStringAsync().Result;
        Dictionary<string, object> json = Net.Parse(raw);
        Dictionary<string, object> data = Net.Dict(json, "data");
        Dictionary<string, object> user = data == null ? null : Net.Dict(data, "user");
        Dictionary<string, object> stream = user == null ? null : Net.Dict(user, "stream");
        if (stream == null) return "не в эфире";
        return "в эфире · " + Net.S(Net.Get(stream, "viewersCount"));
      }
    }

    static string YoutubeAudience(string input)
    {
      string video = Net.Match(input, "(?:v=|youtu\\.be/|/live/|/shorts/)([\\w-]{11})");
      if (video == null && Regex.IsMatch(input.Trim(), "^[\\w-]{11}$")) video = input.Trim();
      if (video == null) return "нет цифры зрителей";
      string html = Net.GetText("https://www.youtube.com/watch?v=" + video, CancellationToken.None).Result;
      string count = Net.Match(html, "originalViewCount\\\\?\"\\s*:\\s*\\\\?\"(\\d+)");
      if (count == null) count = Net.Match(html, "\"originalViewCount\":\"(\\d+)\"");
      bool live = html.IndexOf("\"isLive\":true", StringComparison.Ordinal) >= 0 || html.IndexOf("watching now", StringComparison.OrdinalIgnoreCase) >= 0;
      if (!live && count == null) return "не в эфире";
      if (count == null) return "в эфире";
      return "в эфире · " + count;
    }

    static string VkAudience(string input)
    {
      string slug = input.Trim();
      slug = Regex.Replace(slug, "^https?://(?:www\\.)?(?:live\\.vkvideo\\.ru|vkvideo\\.ru)/", "", RegexOptions.IgnoreCase);
      int slash = slug.IndexOf('/');
      if (slash >= 0) slug = slug.Substring(0, slash);
      if (slug.StartsWith("@")) slug = slug.Substring(1);
      string raw = Net.GetText("https://api.live.vkvideo.ru/v1/blog/" + Uri.EscapeDataString(slug) + "/public_video_stream", CancellationToken.None).Result;
      Dictionary<string, object> json = Net.Parse(raw);
      if (json == null) return "не в эфире";
      string onlineText = Net.S(Net.Get(json, "isOnline")).ToLowerInvariant();
      string endedText = Net.S(Net.Get(json, "isEnded")).ToLowerInvariant();
      bool online = (onlineText == "true" || onlineText == "1") && endedText != "true" && endedText != "1";
      Dictionary<string, object> count = Net.Dict(json, "count");
      string viewers = count == null ? "" : Net.S(Net.Get(count, "viewers"));
      if (!online) return "не в эфире";
      if (viewers.Length == 0) return "в эфире";
      return "в эфире · " + viewers;
    }

    static string GoodgameAudience(string input)
    {
      string name = input.Trim();
      name = Regex.Replace(name, "^https?://(?:www\\.)?goodgame\\.ru/", "", RegexOptions.IgnoreCase);
      int slash = name.IndexOf('/');
      if (slash >= 0) name = name.Substring(0, slash);
      try
      {
        string raw = Net.GetText("https://goodgame.ru/api/4/users/" + Uri.EscapeDataString(name) + "/stream", CancellationToken.None).Result;
        Dictionary<string, object> json = Net.Parse(raw);
        if (json == null) return "не в эфире";
        string viewers = Net.S(Net.Get(json, "viewers"));
        if (viewers.Length == 0) viewers = Net.S(Net.Get(json, "viewers_count"));
        if (viewers.Length == 0) return "в эфире";
        return "в эфире · " + viewers;
      }
      catch
      {
        return "не в эфире";
      }
    }

    static string RutubeAudience(string input)
    {
      string id = FeedEngine.FindRutubeLive(input, CancellationToken.None).Result;
      if (id == null || id.Length == 0) return "не в эфире";
      return "в эфире";
    }

    void ToggleListen()
    {
      listening = !listening;
      ApplyListen();
    }

    void ApplyListen()
    {
      if (liveButton != null)
      {
        liveButton.Text = listening ? "Стоп" : "Старт";
        liveButton.BackColor = listening ? Raised : Live;
        liveButton.ForeColor = listening ? Gold : OnLive;
        liveButton.FlatAppearance.BorderColor = listening ? Gold : Live;
      }
      if (tray != null && tray.ContextMenuStrip != null)
      {
        ToolStripItem item = tray.ContextMenuStrip.Items["live"];
        if (item != null) item.Text = listening ? "Стоп" : "Старт";
      }
      if (listening)
      {
        RefreshFeed();
        if (audienceTimer != null && !audienceTimer.Enabled) audienceTimer.Start();
        PollAudience();
      }
      else
      {
        engine.StopAll();
        if (audienceTimer != null) audienceTimer.Stop();
        foreach (Control control in sourceList.Controls)
        {
          foreach (Control child in control.Controls)
          {
            if (child.Name == "status")
            {
              child.Text = "на стопе";
              child.ForeColor = Mute;
            }
            if (child.Name == "audience") child.Text = "не ищу";
          }
        }
      }
      RefreshState();
    }

    void UpdateStatus(string id, string state, string detail)
    {
      if (!listening) return;
      foreach (Control control in sourceList.Controls)
      {
        if (!string.Equals(Convert.ToString(control.Tag), id, StringComparison.Ordinal)) continue;
        foreach (Control child in control.Controls)
        {
          if (child.Name == "status")
          {
            child.Text = detail == null || detail.Length == 0 ? state : detail;
            child.ForeColor = state == "reconnecting" || state == "error" ? Live : Mute;
          }
        }
      }
    }

    void RefreshFeed()
    {
      if (!listening)
      {
        engine.StopAll();
        return;
      }
      List<SourceConfig> copy = new List<SourceConfig>();
      foreach (SourceConfig source in sources)
      {
        SourceConfig item = new SourceConfig();
        item.Id = source.Id;
        item.Platform = source.Platform;
        item.Input = source.Input;
        item.Enabled = source.Enabled;
        item.Voice = source.Voice;
        copy.Add(item);
      }
      engine.Restart(copy);
    }

    void AddMessage(ChatMessage message)
    {
      if (message == null || message.Id == null) return;
      if (seen.Contains(message.Id)) return;
      if (IsMuted(message.Author)) return;
      if (Rewards.Echo(message.Author, message.Text, message.Platform)) return;
      seen.Add(message.Id);
      if (seen.Count > 4000) seen.Clear();
      Row row = new Row();
      row.Mark = PlatformMark(message.Platform);
      row.Platform = message.Platform;
      row.Author = Swear.Apply(message.Author, swearBox == null || swearBox.Checked);
      row.Text = Swear.Apply(message.Text, swearBox == null || swearBox.Checked);
      row.Icons = message.Icons;
      EmoteArt.Want(row.Icons);
      row.Time = FormatTime(message.Ts);
      chat.Items.Add(row);
      while (chat.Items.Count > 400) chat.Items.RemoveAt(0);
      emptyLabel.Visible = chat.Items.Count == 0;
      SyncHub();
      if (stick && chat.Items.Count > 0) chat.TopIndex = chat.Items.Count - 1;
      else if (!stick)
      {
        downButton.Visible = true;
        downButton.BringToFront();
      }
      if (message.Fresh) SpeakLater(message);
      if (message.Fresh) ConsiderLucy(message);
    }

    void ConsiderLucy(ChatMessage message)
    {
      if (lucyBox == null || !lucyBox.Checked || message == null) return;
      if (message.Platform == "lucy") return;
      if (Rewards.IsOwn(message.Author)) return;
      if (!LucyLocal.Ready || lucyBusy) return;
      if (DateTime.UtcNow < lucyReadyAt) return;
      string heard = message.Text == null ? "" : message.Text.Replace("\uFFFC", " ");
      heard = Regex.Replace(heard, "\\s+", " ").Trim();
      if (!Regex.IsMatch(heard, "(?<![a-zа-яё])(люси|люся|lucy)(?![a-zа-яё])", RegexOptions.IgnoreCase)) return;
      string author = message.Author == null ? "зритель" : message.Author;
      string facts = lucyFacts == null ? LucyLocal.Card : lucyFacts.Text;
      Rewards.Apply(RewardPrefsNow());
      lucyBusy = true;
      lucyReadyAt = DateTime.UtcNow.AddSeconds(45);
      if (lucyState != null) lucyState.Text = "Люси думает…";
      Thread thread = new Thread(new ThreadStart(delegate
      {
        string answer = "";
        try
        {
          answer = LucyLocal.Reply(author + ": " + heard, facts);
          if (answer != null && answer.Length > 0) LucyLore.Remember(author + ": " + heard, answer);
        }
        catch (Exception ex)
        {
          answer = "";
          LucyLocal.LastNote = ex.Message;
        }
        string say = answer;
        string note = LucyLocal.LastNote;
        string posted = say.Length == 0 ? "" : Rewards.PostToTwitch(say);
        try
        {
          BeginInvoke(new Action(delegate
          {
            lucyBusy = false;
            if (say.Length == 0)
            {
              lucyReadyAt = DateTime.UtcNow;
              if (lucyState != null) lucyState.Text = note != null && note.Length > 0 ? note : "Люси промолчала. Позови ещё раз.";
              return;
            }
            if (lucyState != null) lucyState.Text = posted.Length > 0 ? posted : "Люси написала в чат Twitch.";
            ChatMessage reply = new ChatMessage();
            reply.Id = "lucy:" + Guid.NewGuid().ToString("N");
            reply.Platform = "lucy";
            reply.Author = "Люси";
            reply.Text = say;
            reply.Ts = Net.Now();
            reply.Fresh = true;
            AddMessage(reply);
          }));
        }
        catch
        {
          lucyBusy = false;
        }
      }));
      thread.IsBackground = true;
      thread.Name = "RechLucy";
      thread.Start();
    }

    void StartLucyInstall()
    {
      if (lucyInstall == null || !lucyInstall.Enabled) return;
      if (LucyLocal.Ready)
      {
        ShowLucyReady();
        return;
      }
      lucyInstall.Enabled = false;
      if (lucyState != null) lucyState.Text = "Качаю Люси…";
      Thread worker = new Thread(new ThreadStart(delegate
      {
        try
        {
          LucyLocal.Install(delegate(string status)
          {
            if (IsDisposed) return;
            BeginInvoke(new Action(delegate
            {
              if (lucyState != null) lucyState.Text = status;
            }));
          });
          BeginInvoke(new Action(delegate { ShowLucyReady(); }));
        }
        catch (Exception ex)
        {
          BeginInvoke(new Action(delegate
          {
            if (lucyInstall != null) lucyInstall.Enabled = true;
            if (lucyState != null) lucyState.Text = ex.Message;
          }));
        }
      }));
      worker.IsBackground = true;
      worker.Name = "RechLucyInstall";
      worker.Start();
    }

    void ShowLucyReady()
    {
      if (lucyState == null || lucyInstall == null) return;
      if (!LucyLocal.Ready)
      {
        lucyState.Text = "Нужна модель поумнее, около 5 ГБ, один раз. Потом чуть-чуть видеокарты, игру не забираю.";
        lucyInstall.Text = "Скачать Люси";
        lucyInstall.Enabled = true;
        return;
      }
      lucyInstall.Text = "Люси скачана";
      lucyInstall.Enabled = false;
      if (LucyLocal.GpuReady)
      {
        lucyState.Text = "Люси на месте. Немного считает видеокарта, остальное процессор.";
        return;
      }
      lucyState.Text = "Люси на месте. Один раз докачаю кусок для видеокарты.";
      Thread gpu = new Thread(new ThreadStart(delegate
      {
        try
        {
          bool got = LucyLocal.EnsureGpu(delegate(string msg)
          {
            try
            {
              BeginInvoke(new Action(delegate
              {
                if (lucyState != null && msg != null && msg.Length > 0) lucyState.Text = msg;
              }));
            }
            catch
            {
            }
          });
          if (!got && !LucyLocal.GpuReady) return;
          BeginInvoke(new Action(delegate
          {
            if (lucyState != null) lucyState.Text = LucyLocal.GpuReady ? "Люси на месте. Немного считает видеокарта, остальное процессор." : "Люси на месте. Видеокарта не подключилась, считает процессор.";
          }));
        }
        catch (Exception ex)
        {
          try
          {
            BeginInvoke(new Action(delegate
            {
              if (lucyState != null) lucyState.Text = ex.Message;
            }));
          }
          catch
          {
          }
        }
      }));
      gpu.IsBackground = true;
      gpu.Name = "RechLucyGpu";
      gpu.Start();
    }

    void RebuildVisible()
    {
      int i;
      for (i = chat.Items.Count - 1; i >= 0; i--)
      {
        Row row = chat.Items[i] as Row;
        if (row != null && IsMuted(row.Author)) chat.Items.RemoveAt(i);
      }
      emptyLabel.Visible = chat.Items.Count == 0;
      chat.Invalidate();
      SyncHub();
    }

    string VoiceFor(string sourceId, string author)
    {
      string voice = "";
      if (sourceId != null)
      {
        foreach (SourceConfig source in sources)
        {
          if (source.Id == sourceId && source.Voice != null) voice = source.Voice;
        }
      }
      if (voice == "gender") return GenderVoice(author);
      return voice;
    }

    static string GenderVoice(string author)
    {
      string name = NameCore(author);
      if (name.Length < 2) return "";
      if (name == "саша" || name == "женя" || name == "валя" || name == "шура") return "";
      if (FemaleName(name)) return "xenia";
      if (MaleName(name)) return "eugene";
      char last = name[name.Length - 1];
      if (last == 'а' || last == 'я')
      {
        if (name == "никита" || name == "илья" || name == "кузьма" || name == "фома" || name == "лука" || name == "савва" || name == "данила") return "eugene";
        return "xenia";
      }
      if (name == "любовь" || name == "нинель" || name == "рахиль" || name.EndsWith("гуль")) return "xenia";
      if (last >= 'а' && last <= 'я' || last == 'ё') return "eugene";
      return "";
    }

    static bool FemaleName(string name)
    {
      string[] names = new string[] { "маша", "катя", "аня", "настя", "лена", "оля", "ира", "даша", "юля", "алина", "кристина", "мария", "анна", "елена", "ольга", "ирина", "светлана", "татьяна", "наталья", "екатерина", "александра", "виктория", "полина", "валерия", "софия", "софья", "дарья", "анастасия", "ксения", "люси", "люся", "алиса", "катя", "марина", "наташа", "олеся", "ксюша" };
      return Has(names, name);
    }

    static bool MaleName(string name)
    {
      string[] names = new string[] { "иван", "пётр", "петр", "сергей", "андрей", "алексей", "дмитрий", "николай", "михаил", "максим", "артём", "артем", "владимир", "павел", "роман", "игорь", "олег", "денис", "антон", "кирилл", "егор", "никита", "илья", "дима", "коля", "саша", "женя" };
      return Has(names, name);
    }

    static bool Has(string[] names, string name)
    {
      int i;
      for (i = 0; i < names.Length; i++) if (names[i] == name) return true;
      return false;
    }

    static string NameCore(string author)
    {
      if (author == null) return "";
      Match found = Regex.Match(author.Trim().ToLowerInvariant(), "[a-zа-яё]{2,}");
      if (!found.Success) return "";
      string word = found.Value;
      if (word[0] >= 'a' && word[0] <= 'z') word = Regex.Replace(word, ".+", TranslitWord);
      return word;
    }

    static int VoiceIndex(string voice)
    {
      if (voice == "xenia") return 1;
      if (voice == "kseniya") return 2;
      if (voice == "baya") return 3;
      if (voice == "aidar") return 4;
      if (voice == "eugene") return 5;
      if (voice == "gender") return 6;
      return 0;
    }

    static string ForVoice(string text)
    {
      if (text == null || text.Length == 0) return "";
      string spelled = SpellShort(text);
      return Regex.Replace(spelled, "[A-Za-z]{2,}", TranslitWord);
    }

    static string TranslitWord(Match match)
    {
      string word = match.Value.ToLowerInvariant();
      word = word.Replace("sch", "щ").Replace("sh", "ш").Replace("ch", "ч").Replace("zh", "ж");
      word = word.Replace("kh", "х").Replace("ts", "ц").Replace("ya", "я").Replace("yu", "ю").Replace("yo", "ё");
      word = word.Replace("ph", "ф").Replace("th", "т").Replace("wh", "в").Replace("gh", "г").Replace("ck", "к");
      word = word.Replace("ee", "и").Replace("oo", "у");
      string[] letters = new string[] { "а", "б", "к", "д", "е", "ф", "г", "х", "и", "дж", "к", "л", "м", "н", "о", "п", "к", "р", "с", "т", "у", "в", "в", "кс", "й", "з" };
      StringBuilder spoken = new StringBuilder();
      int i;
      for (i = 0; i < word.Length; i++)
      {
        char c = word[i];
        if (c >= 'а' && c <= 'я' || c == 'ё')
        {
          spoken.Append(c);
          continue;
        }
        int index = c - 'a';
        if (index < 0 || index >= letters.Length) continue;
        spoken.Append(letters[index]);
      }
      if (spoken.Length == 0) return match.Value;
      return spoken.ToString();
    }

    static string SpellCaps(string line)
    {
      if (line == null || line.Length == 0) return "";
      string source = line;
      return Regex.Replace(source, "(?<![A-Za-z])[A-Za-z]{2,4}(?![A-Za-z])", delegate(Match match)
      {
        string word = match.Value;
        bool caps = word.ToUpperInvariant() == word && Regex.IsMatch(word, "[A-Z]");
        if (!caps) return word;
        string upper = word.ToUpperInvariant();
        if (upper.Length == 2)
        {
          if (" OK NO HI WE ME MY BE DO GO SO TO UP US AM AN AS AT BY IF IN IS IT OF ON OR HE AH OH ".IndexOf(" " + upper + " ", StringComparison.Ordinal) >= 0) return word;
        }
        else if (upper != "AFK" && upper != "BRB" && upper != "OMG" && upper != "LOL" && upper != "THX" && upper != "IDK" && upper != "IMO" && upper != "TBH" && upper != "PLS" && upper != "WTF" && upper != "GTG" && upper != "NVM" && upper != "SMH" && upper != "LMAO" && upper != "ROFL")
        {
          return word;
        }
        int from = Math.Max(0, match.Index - 20);
        int to = Math.Min(source.Length, match.Index + match.Length + 20);
        string window = source.Substring(from, to - from);
        int cyr = Regex.Matches(window, "[А-Яа-яЁё]").Count;
        int lat = Regex.Matches(window, "[A-Za-z]").Count;
        bool russian = cyr > lat;
        if (upper == "LOL") return russian ? "лол" : "lol";
        if (upper == "OMG") return russian ? "о май гад" : "oh my god";
        if (upper == "THX" || upper == "TY") return russian ? "спасибо" : "thanks";
        return russian ? SpellWord(match).ToString() : SpellEnglish(upper);
      });
    }

    static string SpellEnglish(string word)
    {
      string[] names = new string[] { "ay", "bee", "see", "dee", "ee", "ef", "gee", "aitch", "eye", "jay", "kay", "el", "em", "en", "oh", "pee", "cue", "ar", "es", "tee", "you", "vee", "double you", "ex", "why", "zed" };
      StringBuilder spoken = new StringBuilder();
      int i;
      for (i = 0; i < word.Length; i++)
      {
        int index = word[i] - 'A';
        if (index < 0 || index >= names.Length) return word;
        if (spoken.Length > 0) spoken.Append(' ');
        spoken.Append(names[index]);
      }
      return spoken.ToString();
    }

    static string SpellShort(string line)
    {
      if (line == null || line.Length == 0) return "";
      return Regex.Replace(line, "(?<![A-Za-z])[A-Za-z]{2,3}(?![A-Za-z])", SpellWord);
    }

    static string SpellWord(Match match)
    {
      string word = match.Value.ToLowerInvariant();
      if (word == "ok" || word == "no" || word == "hi" || word == "yo") return match.Value;
      if (word == "lol") return "лол";
      string[] names = new string[] { "эй", "би", "си", "ди", "и", "эф", "джи", "эйч", "ай", "джей", "кей", "эл", "эм", "эн", "о", "пи", "кью", "ар", "эс", "ти", "ю", "ви", "дабл-ю", "икс", "уай", "зед" };
      StringBuilder spoken = new StringBuilder();
      int i;
      for (i = 0; i < word.Length; i++)
      {
        int index = word[i] - 'a';
        if (index < 0 || index >= names.Length) return match.Value;
        if (spoken.Length > 0) spoken.Append(' ');
        spoken.Append(names[index]);
      }
      return spoken.ToString();
    }

    bool IsMuted(string author)
    {
      if (author == null) return false;
      string name = author.Trim().ToLowerInvariant();
      string[] parts = muteBox.Text.Split(new char[] { '\n', '\r', ',' });
      foreach (string part in parts)
      {
        if (part.Trim().ToLowerInvariant() == name && name.Length > 0) return true;
      }
      return false;
    }

    bool englishLine;

    void SpeakLater(ChatMessage message)
    {
      string raw = message.Text == null ? "" : message.Text;
      if (OnlyLink(raw)) return;
      if (Repeat(raw)) return;
      string line = Spoken(message.Author, raw);
      if (line.Length == 0) return;
      bool donate = message.Platform == "da" || message.Platform == "boosty";
      string voice = message.Platform == "lucy" ? "xenia" : VoiceFor(message.SourceId, message.Author);
      bool english = englishLine;
      if (voicePaused) return;
      int wait = delayBox == null ? 0 : (int)delayBox.Value;
      if (wait <= 0)
      {
        if (!voicePaused) speaker.Say(line, false, voice, english, null, donate);
        return;
      }
      Thread later = new Thread(new ThreadStart(delegate
      {
        try { Thread.Sleep(wait * 1000); } catch { return; }
        if (voicePaused) return;
        speaker.Say(line, false, voice, english, null, donate);
      }));
      later.IsBackground = true;
      later.Name = "RechDelay";
      later.Start();
    }

    static bool OnlyLink(string text)
    {
      if (text == null) return false;
      string left = Regex.Replace(text, "https?://\\S+", " ");
      left = Regex.Replace(left, "www\\.[\\w.-]+\\S*", " ", RegexOptions.IgnoreCase);
      left = Regex.Replace(left, "(?:discord\\.gg|t\\.me)/\\S+", " ", RegexOptions.IgnoreCase);
      left = Regex.Replace(left, "\\s+", " ").Trim();
      return left.Length == 0 && text.Trim().Length > 0;
    }

    bool Repeat(string text)
    {
      string key = Regex.Replace(text == null ? "" : text, "\\s+", " ").Trim().ToLowerInvariant();
      if (key.Length == 0) return false;
      if (key == lastTalk) return true;
      lastTalk = key;
      return false;
    }

    string SpokenName(string author)
    {
      if (author == null) return "";
      string raw = author.Trim();
      if (nickBox == null || raw.Length == 0) return raw;
      string[] rows = nickBox.Text.Split(new char[] { '\n', '\r' });
      int i;
      for (i = 0; i < rows.Length; i++)
      {
        int eq = rows[i].IndexOf('=');
        if (eq < 1) continue;
        string left = rows[i].Substring(0, eq).Trim();
        string right = rows[i].Substring(eq + 1).Trim();
        if (left.Length == 0 || right.Length == 0) continue;
        if (string.Equals(left, raw, StringComparison.OrdinalIgnoreCase)) return right;
      }
      return raw;
    }

    string Spoken(string author, string text)
    {
      englishLine = false;
      string line = text == null ? "" : text;
      line = line.Replace("\uFFFC", " ");
      line = Regex.Replace(line, "https?://\\S+", " ");
      line = Regex.Replace(line, "www\\.[\\w.-]+\\S*", " ", RegexOptions.IgnoreCase);
      line = Regex.Replace(line, "(?:discord\\.gg|t\\.me)/\\S+", " ", RegexOptions.IgnoreCase);
      if (emoteBox.Checked) line = Regex.Replace(line, ":[a-z0-9_]{2,}:", " ", RegexOptions.IgnoreCase);
      line = Regex.Replace(line, "\\s+", " ").Trim();
      bool keepEnglish = englishBox != null && englishBox.Checked;
      bool keepNumbers = numberBox == null || numberBox.Checked;
      if (!keepEnglish) line = Regex.Replace(line, "[A-Za-z][A-Za-z'\\-]*", " ");
      else line = SpellCaps(line);
      line = ApplyNumbers(line, keepEnglish, keepNumbers);
      line = Regex.Replace(line, "\\s+", " ").Trim();
      if (!keepEnglish) line = ForVoice(line);
      if (line.Length > 220) line = line.Substring(0, 220) + "…";
      if (line.Length == 0) return "";
      string said;
      if (authorBox.Checked)
      {
        string who = SpokenName(author);
        if (!keepEnglish) who = ForVoice(who);
        if (who.Length == 0) said = line;
        else said = who + ". " + line;
      }
      else said = line;
      if (swearBox == null || swearBox.Checked) said = Swear.Apply(said, true);
      return said;
    }

    static string ApplyNumbers(string text, bool keepEnglish, bool keep)
    {
      if (text == null) return "";
      if (!keep) return Regex.Replace(text, "\\d+", " ");
      return Regex.Replace(text, "\\d+", delegate(Match match)
      {
        long value;
        if (!long.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value < 0 || value > 999999999) return match.Value;
        int from = Math.Max(0, match.Index - 16);
        int to = Math.Min(text.Length, match.Index + match.Length + 16);
        string window = text.Substring(from, to - from);
        int latin = Regex.Matches(window, "[A-Za-z]").Count;
        int cyr = Regex.Matches(window, "[А-Яа-яЁё]").Count;
        bool english = keepEnglish && SileroLocal.EnglishReady && latin > cyr;
        return english ? EnglishNumber(value) : RussianNumber(value);
      });
    }

    static string RussianNumber(long value)
    {
      if (value >= 1000000)
      {
        long millions = value / 1000000;
        string head = RussianChunk(millions);
        long mod = millions % 100;
        string word = "миллионов";
        if (mod < 11 || mod > 14)
        {
          if (millions % 10 == 1) word = "миллион";
          else if (millions % 10 >= 2 && millions % 10 <= 4) word = "миллиона";
        }
        string tail = value % 1000000 == 0 ? "" : " " + RussianNumber(value % 1000000);
        return (head + " " + word + tail).Trim();
      }
      if (value >= 1000)
      {
        long thousands = value / 1000;
        string head = ThousandGender(RussianChunk(thousands));
        long mod = thousands % 100;
        string word = "тысяч";
        if (mod < 11 || mod > 14)
        {
          if (thousands % 10 == 1) word = "тысяча";
          else if (thousands % 10 >= 2 && thousands % 10 <= 4) word = "тысячи";
        }
        string tail = value % 1000 == 0 ? "" : " " + RussianChunk(value % 1000);
        return (head + " " + word + tail).Trim();
      }
      return RussianChunk(value);
    }

    static string ThousandGender(string head)
    {
      if (head == null || head.Length == 0) return "";
      if (head == "один" || head.EndsWith(" один")) return head.Substring(0, head.Length - 4) + "одна";
      if (head == "два" || head.EndsWith(" два")) return head.Substring(0, head.Length - 3) + "две";
      return head;
    }

    static string RussianChunk(long value)
    {
      if (value == 0) return "ноль";
      string[] ones = new string[] { "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять", "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать" };
      string[] tens = new string[] { "", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто" };
      string[] hundreds = new string[] { "", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот" };
      StringBuilder spoken = new StringBuilder();
      if (value >= 100)
      {
        spoken.Append(hundreds[value / 100]);
        value = value % 100;
        if (value > 0) spoken.Append(' ');
      }
      if (value >= 20)
      {
        spoken.Append(tens[value / 10]);
        if (value % 10 > 0) spoken.Append(' ').Append(ones[value % 10]);
      }
      else if (value > 0) spoken.Append(ones[value]);
      return spoken.ToString();
    }

    static string EnglishNumber(long value)
    {
      if (value == 0) return "zero";
      if (value >= 1000000)
      {
        string tail = value % 1000000 == 0 ? "" : " " + EnglishNumber(value % 1000000);
        return (EnglishNumber(value / 1000000) + " million" + tail).Trim();
      }
      string[] ones = new string[] { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
      string[] tens = new string[] { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
      StringBuilder spoken = new StringBuilder();
      if (value >= 1000)
      {
        spoken.Append(EnglishNumber(value / 1000)).Append(" thousand");
        value = value % 1000;
        if (value > 0) spoken.Append(' ');
      }
      if (value >= 100)
      {
        spoken.Append(ones[value / 100]).Append(" hundred");
        value = value % 100;
        if (value > 0) spoken.Append(' ');
      }
      if (value >= 20)
      {
        spoken.Append(tens[value / 10]);
        if (value % 10 > 0) spoken.Append(' ').Append(ones[value % 10]);
      }
      else if (value > 0) spoken.Append(ones[value]);
      return spoken.ToString();
    }

    static string ToLatin(string text)
    {
      if (text == null) return "";
      string[] map = new string[] { "a", "b", "v", "g", "d", "e", "yo", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t", "u", "f", "h", "ts", "ch", "sh", "sch", "", "y", "", "e", "yu", "ya" };
      StringBuilder spoken = new StringBuilder();
      int i;
      string lower = text.ToLowerInvariant();
      for (i = 0; i < lower.Length; i++)
      {
        char c = lower[i];
        if (c == 'ё')
        {
          spoken.Append("yo");
          continue;
        }
        int index = c - 'а';
        if (index >= 0 && index < map.Length) spoken.Append(map[index]);
        else spoken.Append(c);
      }
      return spoken.ToString();
    }

    void DrawBadge(Graphics graphics, Rectangle bounds, string platform, Color back)
    {
      Color ink = BadgeInk(platform);
      using (SolidBrush brush = new SolidBrush(ink))
      {
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillEllipse(brush, bounds);
      }
      string mark = PlatformMark(platform);
      if (mark.Length > 2) mark = mark.Substring(0, 2);
      TextRenderer.DrawText(graphics, mark, Font, bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    public static Color BadgeInk(string platform)
    {
      if (platform == "youtube") return Color.FromArgb(220, 40, 40);
      if (platform == "vk") return Color.FromArgb(0, 119, 255);
      if (platform == "goodgame") return Color.FromArgb(46, 160, 80);
      if (platform == "rutube") return Color.FromArgb(240, 90, 20);
      if (platform == "da") return Color.FromArgb(255, 140, 0);
      if (platform == "boosty") return Color.FromArgb(240, 70, 130);
      if (platform == "lucy") return Color.FromArgb(255, 122, 60);
      if (platform == "wtv") return Color.FromArgb(40, 150, 210);
      if (platform == "steam") return Color.FromArgb(30, 55, 90);
      return Color.FromArgb(145, 70, 255);
    }

    void DrawRow(object sender, DrawItemEventArgs e)
    {
      if (e.Index < 0 || e.Index >= chat.Items.Count) return;
      Row row = chat.Items[e.Index] as Row;
      if (row == null) return;
      bool chroma = capture && chromaBox.Checked;
      Color bg = chroma ? MagentaKey : Canvas;
      using (SolidBrush brush = new SolidBrush(bg)) e.Graphics.FillRectangle(brush, e.Bounds);
      if (!chroma)
      {
        using (Pen pen = new Pen(Line)) e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
      }
      int x = e.Bounds.Left + 12;
      int y = e.Bounds.Top + 6;
      if (platformMarkBox.Checked)
      {
        DrawBadge(e.Graphics, new Rectangle(x, y, 22, 22), row.Platform, bg);
        x += 30;
      }
      TextRenderer.DrawText(e.Graphics, row.Author, boldFont, new Rectangle(x, y, e.Bounds.Width - x - 70, 20), Ink, bg, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
      if (timeBox.Checked)
      {
        TextRenderer.DrawText(e.Graphics, row.Time, Font, new Rectangle(e.Bounds.Right - 64, y, 52, 20), Mute, bg, TextFormatFlags.Right | TextFormatFlags.NoPadding);
      }
      DrawEmotes(e.Graphics, row.Text, row.Icons, Font, new Rectangle(e.Bounds.Left + 12, y + 22, e.Bounds.Width - 24, 28), Ink, bg);
    }

    void DrawEmotes(Graphics g, string text, string icons, Font font, Rectangle area, Color color, Color bg)
    {
      if (text == null) text = "";
      string[] urls = icons == null || icons.Length == 0 ? new string[0] : icons.Split('\n');
      int mark = 0;
      int i = 0;
      int x = area.X;
      TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
      while (i < text.Length && x < area.Right - 4)
      {
        int next = text.IndexOf('\uFFFC', i);
        if (next < 0) next = text.Length;
        if (next > i)
        {
          string chunk = text.Substring(i, next - i);
          Size size = TextRenderer.MeasureText(g, chunk, font, new Size(int.MaxValue, area.Height), flags);
          int room = area.Right - x;
          if (size.Width >= room)
          {
            TextRenderer.DrawText(g, chunk, font, new Rectangle(x, area.Y, room, area.Height), color, bg, flags | TextFormatFlags.EndEllipsis);
            return;
          }
          TextRenderer.DrawText(g, chunk, font, new Rectangle(x, area.Y, size.Width + 4, area.Height), color, bg, flags);
          x += size.Width;
        }
        if (next >= text.Length) break;
        string url = mark < urls.Length ? urls[mark] : "";
        mark = mark + 1;
        if (!EmoteArt.Failed(url))
        {
          int side = Math.Min(22, area.Height);
          if (x + side > area.Right) return;
          Image image = EmoteArt.Peek(url);
          if (image != null)
          {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, new Rectangle(x, area.Y + (area.Height - side) / 2, side, side));
          }
          x += side + 3;
        }
        i = next + 1;
      }
    }

    void UpdateStick()
    {
      int visible = Math.Max(1, chat.ClientSize.Height / Math.Max(1, chat.ItemHeight));
      stick = chat.TopIndex >= chat.Items.Count - visible - 1;
      if (stick) downButton.Visible = false;
    }

    string FirstTwitch()
    {
      foreach (SourceConfig source in sources)
      {
        if (source == null || !source.Enabled || source.Platform != "twitch" || source.Input == null) continue;
        string channel = source.Input.Trim().ToLowerInvariant();
        channel = Regex.Replace(channel, "^https?://(www\\.)?twitch\\.tv/", "");
        int slash = channel.IndexOf('/');
        if (slash >= 0) channel = channel.Substring(0, slash);
        if (channel.StartsWith("#")) channel = channel.Substring(1);
        if (channel.Length > 0) return channel;
      }
      return "";
    }

    RewardPrefs RewardPrefsNow()
    {
      RewardPrefs prefs = new RewardPrefs();
      prefs.ClientId = twitchClient == null ? "" : twitchClient.Text.Trim();
      prefs.Token = twitchToken == null ? "" : twitchToken;
      prefs.Refresh = twitchRefresh == null ? "" : twitchRefresh;
      prefs.SongTitle = songRewardBox == null ? "Заказать песню" : songRewardBox.Text.Trim();
      prefs.LucyTitle = lucyRewardBox == null ? "Поговорить с Люси" : lucyRewardBox.Text.Trim();
      int seconds = 75;
      if (songSecondsBox != null) int.TryParse(songSecondsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds);
      if (seconds < 20) seconds = 20;
      if (seconds > 180) seconds = 180;
      prefs.Seconds = seconds;
      prefs.Volume = songVolumeBar == null ? 100 : songVolumeBar.Value;
      prefs.Youtube = songYtBox == null || songYtBox.Checked;
      prefs.Rutube = songRuBox == null || songRuBox.Checked;
      prefs.Vk = songVkBox == null || songVkBox.Checked;
      prefs.Facts = lucyFacts == null ? LucyLocal.Card : lucyFacts.Text;
      prefs.BotToken = twitchBotToken == null ? "" : twitchBotToken;
      prefs.BotRefresh = twitchBotRefresh == null ? "" : twitchBotRefresh;
      prefs.BotLogin = twitchBotLogin == null ? "" : twitchBotLogin;
      prefs.Channel = FirstTwitch();
      return prefs;
    }

    void OnRewardLine(string author, string text, bool speak)
    {
      if (IsDisposed) return;
      if (InvokeRequired)
      {
        BeginInvoke(new Action(delegate { OnRewardLine(author, text, speak); }));
        return;
      }
      ChatMessage message = new ChatMessage();
      message.Id = "reward:" + Guid.NewGuid().ToString("N");
      message.SourceId = "reward";
      message.Platform = "lucy";
      message.Author = author == null || author.Length == 0 ? "Люси" : author;
      message.Text = text == null ? "" : text;
      message.Ts = Net.Now();
      message.Fresh = false;
      AddMessage(message);
      if (speak)
      {
        speaker.Say(message.Text, true, "xenia");
        string line = message.Text;
        Thread post = new Thread(delegate()
        {
          string note = Rewards.PostToTwitch(line);
          if (note == null || note.Length == 0 || IsDisposed) return;
          try
          {
            BeginInvoke(new Action(delegate { if (lucyState != null) lucyState.Text = note; }));
          }
          catch
          {
          }
        });
        post.IsBackground = true;
        post.Name = "LucyTwitch";
        post.Start();
      }
    }

    void RefreshState()
    {
      string text;
      if (!listening) text = "на стопе";
      else if (voicePaused) text = "слушает · голос на паузе";
      else if (speaker.Speaking) text = "слушает · говорит";
      else if (speaker.Queued > 0) text = "слушает · в очереди " + speaker.Queued.ToString(CultureInfo.InvariantCulture);
      else text = "слушает";
      if (speaker.Skipped > 0) text = text + " · пропущено " + speaker.Skipped.ToString(CultureInfo.InvariantCulture);
      int live = 0;
      foreach (SourceConfig source in sources) if (source.Enabled) live = live + 1;
      if (live > 0) text = text + " · каналов " + live.ToString(CultureInfo.InvariantCulture);
      stateLabel.Text = text;
      if (rewardState != null) rewardState.Text = Rewards.Status;
      if (botState != null) botState.Text = Rewards.BotStatus;
      if (songNow != null) songNow.Text = Rewards.NowText();
      ClipWindow.Sync(Rewards.CurrentEmbed());
      ObsEye.Want(Rewards.CurrentEmbed().Length > 0);
      if (speaker.LastError != null && speaker.LastError.Length > 0) errorLabel.Text = speaker.LastError;
      else errorLabel.Text = "";
    }

    bool HasRussianVoice()
    {
      int i;
      for (i = 0; i < voiceBox.Items.Count; i++)
      {
        string label = Convert.ToString(voiceBox.Items[i]);
        if (label == null) continue;
        if (label.IndexOf("ru-RU", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (label.IndexOf("Russian", StringComparison.OrdinalIgnoreCase) >= 0) return true;
      }
      return false;
    }

    void SetCapture(bool on)
    {
      capture = on;
      topBar.Visible = !on;
      left.Visible = !on;
      right.Visible = !on;
      FormBorderStyle = on ? FormBorderStyle.None : FormBorderStyle.Sizable;
      hintChip.Visible = on;
      hintChip.BringToFront();
      TopMost = on || topBox.Checked;
      ApplyCaptureColors();
      if (on) Focus();
    }

    void ApplyCaptureColors()
    {
      bool chroma = capture && chromaBox.Checked;
      Color bg = chroma ? MagentaKey : Canvas;
      BackColor = bg;
      center.BackColor = bg;
      chat.BackColor = bg;
      emptyLabel.BackColor = bg;
      chat.Invalidate();
    }

    void RestoreStudio()
    {
      SetCapture(false);
      Show();
      WindowState = FormWindowState.Normal;
      Activate();
    }

    void UpdatePlaceholder()
    {
      string[] hints = new string[] {
        "ник канала",
        "@канал или ссылка",
        "ник или ссылка",
        "ник",
        "канал или ссылка на эфир",
        "ник канала",
        "ссылка на профиль",
        "ссылка виджета или токен",
        "boosty.to/ник"
      };
      int index = platformBox.SelectedIndex;
      if (index < 0 || index >= hints.Length) index = 0;
      if (channelBox.IsHandleCreated)
      {
        try
        {
          SendMessageCue(channelBox.Handle, 0x1501, (IntPtr)1, hints[index]);
        }
        catch
        {
        }
      }
    }

    protected override void OnShown(EventArgs e)
    {
      base.OnShown(e);
      UpdatePlaceholder();
    }

    void SaveSettings()
    {
      if (loading) return;
      Settings settings = new Settings();
      settings.Sources = sources;
      settings.TtsEnabled = ttsBox.Checked;
      settings.Engine = "silero";
      settings.ReadAuthor = authorBox.Checked;
      settings.Volume = volumeBar.Value;
      settings.RatePercent = rateBar.Value;
      settings.VoiceName = voiceBox.SelectedIndex >= 0 && voiceBox.SelectedIndex < voiceNames.Count ? voiceNames[voiceBox.SelectedIndex] : "";
      settings.SileroUrl = sileroUrl.Text.Trim();
      settings.SileroOutputs = sileroOutputs.Text.Trim();
      settings.SileroSpeaker = SileroSpeaker(sileroBox.SelectedIndex < 0 ? 0 : sileroBox.SelectedIndex);
      settings.SkipEmotes = emoteBox.Checked;
      settings.LucyOn = lucyBox != null && lucyBox.Checked;
      settings.LucyFacts = lucyFacts == null ? LucyLocal.Card : lucyFacts.Text;
      settings.TwitchClientId = twitchClient == null ? "" : twitchClient.Text.Trim();
      settings.TwitchToken = twitchToken == null ? "" : twitchToken;
      settings.TwitchRefresh = twitchRefresh == null ? "" : twitchRefresh;
      settings.TwitchBotToken = twitchBotToken == null ? "" : twitchBotToken;
      settings.TwitchBotRefresh = twitchBotRefresh == null ? "" : twitchBotRefresh;
      settings.TwitchBotLogin = twitchBotLogin == null ? "" : twitchBotLogin;
      settings.SongReward = songRewardBox == null ? "Заказать песню" : songRewardBox.Text.Trim();
      settings.LucyReward = lucyRewardBox == null ? "Поговорить с Люси" : lucyRewardBox.Text.Trim();
      int songSeconds = 75;
      if (songSecondsBox != null) int.TryParse(songSecondsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out songSeconds);
      if (songSeconds < 20) songSeconds = 20;
      if (songSeconds > 180) songSeconds = 180;
      settings.SongSeconds = songSeconds;
      settings.SongVolume = songVolumeBar == null ? 100 : songVolumeBar.Value;
      settings.SongYoutube = songYtBox == null || songYtBox.Checked;
      settings.SongRutube = songRuBox == null || songRuBox.Checked;
      settings.SongVk = songVkBox == null || songVkBox.Checked;
      Rewards.Apply(RewardPrefsNow());
      settings.HideSwear = swearBox == null || swearBox.Checked;
      settings.ReadEnglish = englishBox.Checked;
      settings.ReadNumbers = numberBox.Checked;
      settings.SpeechDelay = (int)delayBox.Value;
      settings.NickMap = nickBox.Text;
      settings.PauseHotkey = pauseMod.ToString(CultureInfo.InvariantCulture) + ":" + ((int)pauseKey).ToString(CultureInfo.InvariantCulture);
      settings.PauseHotkeyOn = pauseKeyBox.Checked;
      settings.MuteUsers = muteBox.Text;
      settings.ShowPlatform = platformMarkBox.Checked;
      settings.ShowTime = timeBox.Checked;
      settings.AlwaysOnTop = topBox.Checked;
      settings.Rehearsal = rehearsalBox.Checked;
      settings.Version = 2;
      settings.Chroma = chromaBox.Checked;
      settings.OverlayFont = fontBox.SelectedIndex >= 0 ? Convert.ToString(fontBox.SelectedItem) : "Segoe UI";
      settings.OverlaySize = fontSizeBar.Value;
      settings.OverlayBold = fontBoldBox.Checked;
      settings.OverlayColor = ColorText(textColorButton.BackColor);
      settings.OverlayNick = ColorText(nickColorButton.BackColor);
      settings.OverlayStroke = StrokeId();
      settings.OverlayPlate = plateBox.Checked;
      settings.OverlayAlign = alignBox.SelectedIndex == 1 ? "top" : "bottom";
      settings.OverlayLines = linesBar.Value;
      settings.OverlayHold = holdBar.Value;
      settings.ChatX = chatX;
      settings.ChatY = chatY;
      settings.ChatW = chatW;
      settings.ChatH = chatH;
      settings.ReaderFont = readerFont;
      settings.ReaderSize = readerSize;
      settings.ReaderColor = ColorText(readerText);
      settings.ReaderNick = ColorText(readerNick);
      settings.ReaderBack = ColorText(readerBack);
      settings.GameHotkey = gameMod.ToString(CultureInfo.InvariantCulture) + ":" + ((int)gameKey).ToString(CultureInfo.InvariantCulture);
      settings.GameDeleteAudio = gameDeleteBox.Checked;
      settings.GameTranslate = gameTranslateBox.Checked;
      settings.GameSpeakLater = gameSpeakBox.Checked;
      settings.GameHotkeyOn = gameHotkeyBox.Checked;
      settings.GameKeepGame = gameSoftBox.Checked;
      settings.GamePicture = gamePictureBox.Checked;
      settings.GamePhraseSec = earPhraseSec;
      if (WindowState == FormWindowState.Normal)
      {
        settings.WinX = Bounds.X;
        settings.WinY = Bounds.Y;
        settings.WinW = Bounds.Width;
        settings.WinH = Bounds.Height;
      }
      try
      {
        string dir = Path.GetDirectoryName(settingsPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(settingsPath, Net.Serialize(settings), Encoding.UTF8);
      }
      catch
      {
      }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
      if (armHotkey)
      {
        if (TakeHotkey(keyData, true)) return true;
      }
      if (armPause)
      {
        if (TakeHotkey(keyData, false)) return true;
      }
      if (keyData == Keys.Escape && capture)
      {
        SetCapture(false);
        return true;
      }
      return base.ProcessCmdKey(ref msg, keyData);
    }

    bool TakeHotkey(Keys keyData, bool game)
    {
      Keys key = keyData & Keys.KeyCode;
      if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return false;
      int mod = 0;
      if ((keyData & Keys.Control) == Keys.Control) mod |= 2;
      if ((keyData & Keys.Shift) == Keys.Shift) mod |= 4;
      if ((keyData & Keys.Alt) == Keys.Alt) mod |= 1;
      if (game)
      {
        gameMod = mod;
        gameKey = key;
        armHotkey = false;
        BindHotkey();
      }
      else
      {
        pauseMod = mod;
        pauseKey = key;
        armPause = false;
        BindPauseKey();
      }
      SaveSettings();
      return true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
      base.OnHandleCreated(e);
      BindHotkey();
      BindPauseKey();
    }

    protected override void WndProc(ref Message m)
    {
      if (m.Msg == 0x0312 && m.WParam.ToInt32() == GameHotId)
      {
        BeginGamePick(true);
        return;
      }
      if (m.Msg == 0x0312 && m.WParam.ToInt32() == PauseHotId)
      {
        TogglePause();
        return;
      }
      base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
      if (!quitting)
      {
        quitting = true;
        SaveSettings();
        try { if (rehearsalTimer != null) rehearsalTimer.Stop(); } catch { }
        try { if (uiTimer != null) uiTimer.Stop(); } catch { }
        try { if (chatWindow != null) chatWindow.Close(); } catch { }
        hub.Stop();
        engine.StopAll();
        speaker.Stop();
        StopEar();
        try { UnregisterHotKey(Handle, GameHotId); } catch { }
        try { UnregisterHotKey(Handle, PauseHotId); } catch { }
        try
        {
          tray.Visible = false;
          tray.Dispose();
        }
        catch
        {
        }
      }
      base.OnFormClosing(e);
    }

    void Shutdown()
    {
      Close();
    }

    void Ui(Action action)
    {
      if (IsDisposed || !IsHandleCreated) return;
      try
      {
        if (InvokeRequired) BeginInvoke(action);
        else action();
      }
      catch
      {
      }
    }

    static string FormatTime(long ts)
    {
      if (ts < 1000000000L) return "";
      try
      {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddMilliseconds(ts).ToLocalTime().ToString("HH:mm");
      }
      catch
      {
        return "";
      }
    }

    static string PlatformId(int index)
    {
      string[] ids = new string[] { "twitch", "youtube", "vk", "goodgame", "rutube", "wtv", "steam", "da", "boosty" };
      if (index < 0 || index >= ids.Length) return "twitch";
      return ids[index];
    }

    static string PlatformLabel(string id)
    {
      if (id == "youtube") return "YouTube";
      if (id == "vk") return "VK Видео";
      if (id == "goodgame") return "GoodGame";
      if (id == "rutube") return "Rutube";
      if (id == "wtv") return "w.tv";
      if (id == "steam") return "Steam";
      if (id == "da") return "DonationAlerts";
      if (id == "boosty") return "Boosty";
      return "Twitch";
    }

    public static string PlatformMark(string id)
    {
      if (id == "youtube") return "YT";
      if (id == "vk") return "VK";
      if (id == "goodgame") return "GG";
      if (id == "rutube") return "RU";
      if (id == "wtv") return "W";
      if (id == "steam") return "ST";
      if (id == "da") return "DA";
      if (id == "boosty") return "BY";
      if (id == "lucy") return "Л";
      return "TW";
    }

    static void Place(Control parent, Control child, int x, ref int y, int height)
    {
      child.SetBounds(x, y, 292, height);
      parent.Controls.Add(child);
      y += height + 8;
    }

    void QuietBar(TrackBar bar)
    {
      bar.TickStyle = TickStyle.None;
      bar.MouseWheel += delegate(object sender, MouseEventArgs e)
      {
        HandledMouseEventArgs handled = e as HandledMouseEventArgs;
        if (handled != null) handled.Handled = true;
        Control parent = bar.Parent;
        while (parent != null)
        {
          ScrollableControl scroll = parent as ScrollableControl;
          if (scroll != null && scroll.AutoScroll)
          {
            int y = -scroll.AutoScrollPosition.Y - Math.Sign(e.Delta) * 48;
            if (y < 0) y = 0;
            scroll.AutoScrollPosition = new Point(0, y);
            break;
          }
          parent = parent.Parent;
        }
      };
    }

    static Image LoadFace()
    {
      try
      {
        System.Reflection.Assembly asm = typeof(MainForm).Assembly;
        using (Stream stream = asm.GetManifestResourceStream("Rech.lucy.png"))
        {
          if (stream == null) return null;
          using (MemoryStream copy = new MemoryStream())
          {
            stream.CopyTo(copy);
            copy.Position = 0;
            using (Image image = Image.FromStream(copy))
            {
              return new Bitmap(image);
            }
          }
        }
      }
      catch
      {
        return null;
      }
    }

    static Label MakeCaption(string text)
    {
      Label label = new Label();
      label.Text = text;
      label.ForeColor = Gold;
      label.Font = new Font("Georgia", 9f, FontStyle.Bold);
      label.TextAlign = ContentAlignment.MiddleLeft;
      return label;
    }

    static Label MakeMute(string text)
    {
      Label label = new Label();
      label.Text = text;
      label.ForeColor = Mute;
      label.TextAlign = ContentAlignment.MiddleLeft;
      return label;
    }

    static Label MakeWrap(string text)
    {
      Label label = new Label();
      label.Text = text;
      label.ForeColor = Mute;
      return label;
    }

    CheckBox MakeCheck(string text)
    {
      CheckBox box = new CheckBox();
      box.Text = text;
      box.ForeColor = Ink;
      box.BackColor = Surface;
      return box;
    }

    static void StyleBox(TextBox box)
    {
      box.BorderStyle = BorderStyle.FixedSingle;
      box.BackColor = Raised;
      box.ForeColor = Ink;
    }

    void ApplyOverlaySettings(Settings settings)
    {
      string font = settings.OverlayFont;
      int index = 0;
      int i;
      if (font != null && font.Length > 0)
      {
        bool found = false;
        for (i = 0; i < fontBox.Items.Count; i++)
        {
          if (string.Equals(Convert.ToString(fontBox.Items[i]), font, StringComparison.OrdinalIgnoreCase))
          {
            index = i;
            found = true;
          }
        }
        if (!found)
        {
          fontBox.Items.Add(font);
          index = fontBox.Items.Count - 1;
        }
      }
      fontBox.SelectedIndex = index;
      int size = settings.OverlaySize;
      if (size < 18 || size > 72) size = 32;
      fontSizeBar.Value = size;
      fontSizeLabel.Text = "Размер " + size.ToString(CultureInfo.InvariantCulture);
      fontBoldBox.Checked = settings.OverlayFont == null || settings.OverlayFont.Length == 0 || settings.OverlayBold;
      PaintSwatch(textColorButton, ParseColor(settings.OverlayColor, Color.FromArgb(246, 239, 228)));
      PaintSwatch(nickColorButton, ParseColor(settings.OverlayNick, Color.FromArgb(255, 122, 60)));
      if (settings.OverlayStroke == "stroke") strokeBox.SelectedIndex = 1;
      else if (settings.OverlayStroke == "none") strokeBox.SelectedIndex = 2;
      else strokeBox.SelectedIndex = 0;
      plateBox.Checked = settings.OverlayPlate;
      alignBox.SelectedIndex = settings.OverlayAlign == "top" ? 1 : 0;
      int lines = settings.OverlayLines;
      if (lines < 3 || lines > 16) lines = 8;
      linesBar.Value = lines;
      linesLabel.Text = "Строк на экране " + lines.ToString(CultureInfo.InvariantCulture);
      int hold = settings.OverlayHold;
      if (hold < 2 || hold > 20) hold = 8;
      holdBar.Value = hold;
      holdLabel.Text = "Держать " + hold.ToString(CultureInfo.InvariantCulture) + " сек";
      if (settings.ChatW >= 160 && settings.ChatH >= 90)
      {
        chatX = settings.ChatX;
        chatY = settings.ChatY;
        chatW = settings.ChatW;
        chatH = settings.ChatH;
      }
      sizing = true;
      chatWBox.Text = chatW.ToString(CultureInfo.InvariantCulture);
      chatHBox.Text = chatH.ToString(CultureInfo.InvariantCulture);
      sizing = false;
      RefreshSizeHint();
      readerFont = settings.ReaderFont == null || settings.ReaderFont.Length == 0 ? "Segoe UI" : settings.ReaderFont;
      readerSize = settings.ReaderSize < 12 || settings.ReaderSize > 40 ? 18 : settings.ReaderSize;
      readerText = ParseColor(settings.ReaderColor, Color.FromArgb(246, 239, 228));
      readerNick = ParseColor(settings.ReaderNick, Color.FromArgb(255, 122, 60));
      readerBack = ParseColor(settings.ReaderBack, Color.FromArgb(17, 14, 12));
    }

    void PaintSwatch(Button button, Color color)
    {
      button.BackColor = color;
      button.ForeColor = Contrast(color);
      button.FlatAppearance.BorderColor = Contrast(color);
    }

    void PickColor(Button button)
    {
      ColorDialog dialog = new ColorDialog();
      dialog.FullOpen = true;
      dialog.Color = button.BackColor;
      if (dialog.ShowDialog(this) != DialogResult.OK) return;
      PaintSwatch(button, dialog.Color);
      PushStyle();
      SaveSettings();
    }

    static Color Contrast(Color color)
    {
      int luma = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
      return luma > 160 ? Color.FromArgb(20, 16, 12) : Color.FromArgb(255, 248, 240);
    }

    static Color ParseColor(string text, Color fallback)
    {
      if (text == null || text.Length == 0) return fallback;
      try
      {
        return ColorTranslator.FromHtml(text);
      }
      catch
      {
        return fallback;
      }
    }

    static string ColorText(Color color)
    {
      return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
    }

    string StrokeId()
    {
      if (strokeBox.SelectedIndex == 1) return "stroke";
      if (strokeBox.SelectedIndex == 2) return "none";
      return "shadow";
    }

    void PushStyle()
    {
      OverlayStyle style = new OverlayStyle();
      style.Font = fontBox.SelectedIndex >= 0 ? Convert.ToString(fontBox.SelectedItem) : "Segoe UI";
      style.Size = fontSizeBar.Value;
      style.Bold = fontBoldBox.Checked;
      style.Color = ColorText(textColorButton.BackColor);
      style.Nick = ColorText(nickColorButton.BackColor);
      style.Stroke = StrokeId();
      style.Plate = plateBox.Checked;
      style.Align = alignBox.SelectedIndex == 1 ? "top" : "bottom";
      style.Lines = linesBar.Value;
      style.Hold = holdBar.Value;
      style.Platform = platformMarkBox.Checked;
      style.Clock = timeBox.Checked;
      hub.SetStyle(style);
    }

    void SyncHub()
    {
      List<OverlayLine> list = new List<OverlayLine>();
      int i;
      for (i = 0; i < chat.Items.Count; i++)
      {
        Row row = chat.Items[i] as Row;
        if (row == null) continue;
        OverlayLine line = new OverlayLine();
        line.Author = row.Author == null ? "" : row.Author;
        line.Text = row.Text == null ? "" : row.Text;
        line.Icons = row.Icons == null ? "" : row.Icons;
        line.Mark = row.Mark == null ? "" : row.Mark;
        line.Time = row.Time == null ? "" : row.Time;
        list.Add(line);
      }
      hub.Replace(list);
    }

    void MarkReader(bool open)
    {
      string text = open ? "Скрыть моё окно" : "Моё окно";
      if (chatWindowButton != null) chatWindowButton.Text = open ? "Скрыть моё окно" : "Моё окно — читать";
      if (topChatButton != null) topChatButton.Text = text;
    }

    void ToggleChatWindow()
    {
      if (chatWindow != null && !chatWindow.IsDisposed)
      {
        RememberChat(chatWindow.Bounds);
        chatWindow.Close();
        return;
      }
      ReaderWindow window = new ReaderWindow(hub);
      window.ApplyLook(readerFont, readerSize, readerText, readerNick, readerBack);
      window.StartPosition = FormStartPosition.Manual;
      int w = chatW < 360 ? 720 : chatW;
      int h = chatH < 240 ? 460 : chatH;
      window.Bounds = new Rectangle(chatX, chatY, w, h);
      window.Placed += delegate(Rectangle bounds) { RememberChat(bounds); };
      window.LookChanged += delegate
      {
        readerFont = window.FontName;
        readerSize = window.FontSize;
        readerText = window.TextColor;
        readerNick = window.NickColor;
        readerBack = window.Paper;
        SaveSettings();
      };
      window.Crowd = delegate { return CrowdNow(); };
      window.FormClosed += delegate
      {
        chatWindow = null;
        MarkReader(false);
      };
      chatWindow = window;
      window.Show();
      MarkReader(true);
    }

    void ApplyTypedSize()
    {
      int w;
      int h;
      if (!int.TryParse(chatWBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out w)) return;
      if (!int.TryParse(chatHBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return;
      if (w < 160) w = 160;
      if (h < 90) h = 90;
      if (w > 1920) w = 1920;
      if (h > 1080) h = 1080;
      chatW = w;
      chatH = h;
      sizing = true;
      chatWBox.Text = w.ToString(CultureInfo.InvariantCulture);
      chatHBox.Text = h.ToString(CultureInfo.InvariantCulture);
      sizing = false;
      if (chatWindow != null && !chatWindow.IsDisposed)
      {
        chatWindow.Bounds = new Rectangle(chatWindow.Left, chatWindow.Top, w, h);
      }
      RefreshSizeHint();
      SaveSettings();
    }

    void RefreshSizeHint()
    {
      if (sizeHint == null) return;
      sizeHint.Text = "Моё окно " + chatW.ToString(CultureInfo.InvariantCulture) + " × " + chatH.ToString(CultureInfo.InvariantCulture) + ". В OBS размер другой — его ставишь у источника.";
    }

    void RememberChat(Rectangle bounds)
    {
      if (bounds.Width < 160 || bounds.Height < 90) return;
      chatX = bounds.X;
      chatY = bounds.Y;
      chatW = bounds.Width;
      chatH = bounds.Height;
      sizing = true;
      if (chatWBox != null)
      {
        chatWBox.Text = chatW.ToString(CultureInfo.InvariantCulture);
        chatHBox.Text = chatH.ToString(CultureInfo.InvariantCulture);
      }
      sizing = false;
      RefreshSizeHint();
      if (!loading) SaveSettings();
    }

    static void StyleCombo(ComboBox box)
    {
      box.FlatStyle = FlatStyle.Flat;
      box.BackColor = Raised;
      box.ForeColor = Ink;
    }

    Button MakeButton(string text, bool primary)
    {
      Button button = new Button();
      button.Text = text;
      button.FlatStyle = FlatStyle.Flat;
      button.FlatAppearance.BorderColor = primary ? Live : Line;
      button.BackColor = primary ? Live : Raised;
      button.ForeColor = primary ? OnLive : Gold;
      button.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
      button.Cursor = Cursors.Hand;
      return button;
    }

    static Icon MakeIcon()
    {
      Bitmap bmp = new Bitmap(32, 32);
      using (Graphics g = Graphics.FromImage(bmp))
      {
        g.Clear(Color.FromArgb(16, 14, 12));
        using (SolidBrush ember = new SolidBrush(Live)) g.FillRectangle(ember, 0, 26, 32, 6);
        using (Font font = new Font("Georgia", 14f, FontStyle.Bold))
        using (SolidBrush brush = new SolidBrush(Ink))
        {
          g.DrawString("Р", font, brush, 5, 2);
        }
      }
      return Icon.FromHandle(bmp.GetHicon());
    }

    void BuildGame()
    {
      modeBar = new Panel();
      modeBar.Dock = DockStyle.Top;
      modeBar.Height = 44;
      modeBar.BackColor = Surface;
      Button chatMode = MakeButton("Чат", true);
      chatMode.SetBounds(8, 6, 88, 32);
      chatMode.Click += delegate { ShowMode(false); };
      Button gameMode = MakeButton("Игра", false);
      gameMode.SetBounds(102, 6, 88, 32);
      gameMode.Click += delegate { ShowMode(true); };
      modeBar.Controls.Add(chatMode);
      modeBar.Controls.Add(gameMode);
      modeBar.Tag = chatMode;
      gameMode.Tag = "game";
      chatMode.Name = "chatMode";
      gameMode.Name = "gameMode";

      gamePanel = new Panel();
      gamePanel.Dock = DockStyle.Fill;
      gamePanel.BackColor = Canvas;
      gamePanel.Visible = false;
      Label lead = new Label();
      lead.Text = "Клавиша прячет окно в трей. Обведи текст поверх всех экранов.";
      lead.ForeColor = Mute;
      lead.Font = new Font("Georgia", 10f, FontStyle.Italic);
      lead.SetBounds(16, 8, 640, 28);
      gameHotkeyButton = MakeButton("Клавиша: Ctrl+Ё", false);
      gameHotkeyButton.SetBounds(16, 42, 220, 34);
      gameHotkeyButton.Click += delegate
      {
        armHotkey = true;
        armPause = false;
        gameHotkeyButton.Text = "Нажми сочетание…";
      };
      Button pickNow = MakeButton("Обвести", true);
      pickNow.SetBounds(244, 42, 120, 34);
      pickNow.Click += delegate { BeginGamePick(false); };
      gameHotkeyBox = MakeCheck("Горячая клавиша");
      gameHotkeyBox.Checked = false;
      gameHotkeyBox.SetBounds(300, 84, 190, 24);
      gameHotkeyBox.CheckedChanged += delegate
      {
        if (!loading)
        {
          BindHotkey();
          SaveSettings();
        }
      };
      gameTranslateBox = MakeCheck("Переводить сразу");
      gameTranslateBox.Checked = true;
      gameTranslateBox.SetBounds(16, 84, 280, 24);
      gameTranslateBox.CheckedChanged += delegate { if (!loading) SaveSettings(); };
      gameSpeakBox = MakeCheck("Говорить по нажатию");
      gameSpeakBox.Checked = false;
      gameSpeakBox.SetBounds(16, 110, 280, 24);
      gameSpeakBox.CheckedChanged += delegate { if (!loading) SaveSettings(); };
      gameDeleteBox = MakeCheck("Удалять запись");
      gameDeleteBox.Checked = false;
      gameDeleteBox.SetBounds(16, 136, 280, 24);
      gameDeleteBox.CheckedChanged += delegate { if (!loading) SaveSettings(); };
      gameSoftBox = MakeCheck("Не сворачивать игру");
      gameSoftBox.Checked = true;
      gameSoftBox.SetBounds(300, 136, 220, 24);
      gameSoftBox.CheckedChanged += delegate { if (!loading) SaveSettings(); };
      gamePictureBox = MakeCheck("Текст на скриншоте");
      gamePictureBox.Checked = false;
      gamePictureBox.SetBounds(300, 110, 220, 24);
      gamePictureBox.CheckedChanged += delegate { if (!loading) SaveSettings(); };
      gameList = new ListBox();
      gameList.IntegralHeight = false;
      gameList.BorderStyle = BorderStyle.None;
      gameList.BackColor = Raised;
      gameList.ForeColor = Ink;
      gameList.SetBounds(16, 168, 640, 220);
      gameList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
      Button again = MakeButton("Ещё раз", false);
      again.SetBounds(16, 426, 120, 34);
      again.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
      again.Click += delegate { ReplayGame(); };
      Button clear = MakeButton("Очистить", false);
      clear.SetBounds(144, 426, 120, 34);
      clear.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
      clear.Click += delegate { ClearGame(); };
      Button dropLine = MakeButton("Удалить", false);
      dropLine.SetBounds(272, 426, 120, 34);
      dropLine.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
      dropLine.Click += delegate { DeleteGame(); };
      gameList.KeyDown += delegate(object sender, KeyEventArgs e)
      {
        if (e.KeyCode == Keys.Delete)
        {
          DeleteGame();
          e.Handled = true;
        }
      };
      gameStatus = new Label();
      gameStatus.ForeColor = Gold;
      gameStatus.Text = "Горячая клавиша выключена. Включи галочку, когда нужно ловить текст.";
      gameStatus.SetBounds(16, 468, 640, 36);
      gameStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
      gamePanel.Controls.Add(lead);
      gamePanel.Controls.Add(gameHotkeyButton);
      gamePanel.Controls.Add(pickNow);
      gamePanel.Controls.Add(gameHotkeyBox);
      gamePanel.Controls.Add(gameTranslateBox);
      gamePanel.Controls.Add(gameSpeakBox);
      gamePanel.Controls.Add(gameDeleteBox);
      gamePanel.Controls.Add(gameSoftBox);
      gamePanel.Controls.Add(gamePictureBox);
      gamePanel.Controls.Add(gameList);
      gamePanel.Controls.Add(again);
      gamePanel.Controls.Add(clear);
      gamePanel.Controls.Add(dropLine);
      gamePanel.Controls.Add(gameStatus);
      gamePanel.Resize += delegate
      {
        int w = Math.Max(200, gamePanel.Width - 32);
        lead.Width = w;
        gameList.Width = w;
        gameStatus.Width = w;
        gameList.Height = Math.Max(80, gamePanel.Height - 168 - 88);
        int bottom = gameList.Bottom + 10;
        again.Top = bottom;
        clear.Top = bottom;
        dropLine.Top = bottom;
        gameStatus.Top = bottom + 40;
      };
      center.Controls.Add(gamePanel);
      center.Controls.Add(modeBar);
      modeBar.BringToFront();
    }

    void ShowMode(bool game)
    {
      gameOn = game;
      if (gamePanel != null) gamePanel.Visible = game;
      chat.Visible = !game;
      emptyLabel.Visible = !game && chat.Items.Count == 0;
      if (game) downButton.Visible = false;
      if (modeBar == null) return;
      foreach (Control control in modeBar.Controls)
      {
        Button button = control as Button;
        if (button == null) continue;
        bool on = (button.Name == "gameMode") == game;
        button.BackColor = on ? Live : Raised;
        button.ForeColor = on ? OnLive : Gold;
        button.FlatAppearance.BorderColor = on ? Live : Line;
      }
    }

    void ApplyGameSettings(Settings settings)
    {
      if (settings.GameHotkey != null)
      {
        string[] pair = settings.GameHotkey.Split(':');
        int mod;
        int key;
        if (pair.Length == 2 && int.TryParse(pair[0], out mod) && int.TryParse(pair[1], out key))
        {
          gameMod = mod;
          gameKey = (Keys)key;
        }
      }
      if (gameDeleteBox != null) gameDeleteBox.Checked = settings.GameDeleteAudio ?? false;
      if (gameTranslateBox != null) gameTranslateBox.Checked = settings.GameTranslate ?? true;
      if (gameSpeakBox != null) gameSpeakBox.Checked = settings.GameSpeakLater ?? false;
      if (gameHotkeyBox != null) gameHotkeyBox.Checked = settings.GameHotkeyOn ?? false;
      if (gameSoftBox != null) gameSoftBox.Checked = settings.GameKeepGame ?? true;
      if (gamePictureBox != null) gamePictureBox.Checked = settings.GamePicture ?? false;
      int phrase = settings.GamePhraseSec ?? 5;
      if (phrase < 3) phrase = 5;
      if (phrase > 8) phrase = 8;
      earPhraseSec = phrase;
      if (earLen != null) earLen.Value = phrase;
      if (earLenLabel != null) earLenLabel.Text = "фраза " + phrase.ToString(CultureInfo.InvariantCulture) + " с";
      BindHotkey();
      BindPauseKey();
    }

    void BindPauseKey()
    {
      if (!IsHandleCreated) return;
      try { UnregisterHotKey(Handle, PauseHotId); } catch { }
      bool on = pauseKeyBox == null || pauseKeyBox.Checked;
      if (!on)
      {
        if (pauseKeyButton != null) pauseKeyButton.Text = "Клавиша выкл: " + KeyText(pauseMod, pauseKey);
        return;
      }
      uint mod = (uint)(pauseMod | 0x4000);
      bool ok = RegisterHotKey(Handle, PauseHotId, mod, (uint)pauseKey);
      if (pauseKeyButton != null) pauseKeyButton.Text = ok ? "Клавиша: " + KeyText(pauseMod, pauseKey) : "Занята: " + KeyText(pauseMod, pauseKey);
    }

    static string KeyText(int mod, Keys key)
    {
      string text = "";
      if ((mod & 2) != 0) text += "Ctrl+";
      if ((mod & 4) != 0) text += "Shift+";
      if ((mod & 1) != 0) text += "Alt+";
      if (key == Keys.Oem3) text += "Ё";
      else text += key.ToString();
      return text;
    }

    void TogglePause()
    {
      voicePaused = !voicePaused;
      speaker.Hold = voicePaused;
      if (voicePaused) speaker.ClearQueue();
      if (pauseButton != null)
      {
        pauseButton.Text = voicePaused ? "Голос на паузе" : "Пауза голоса";
        pauseButton.BackColor = voicePaused ? Live : Raised;
        pauseButton.ForeColor = voicePaused ? OnLive : Gold;
        pauseButton.FlatAppearance.BorderColor = voicePaused ? Live : Line;
      }
      RefreshState();
    }

    void BindHotkey()
    {
      if (!IsHandleCreated) return;
      try { UnregisterHotKey(Handle, GameHotId); } catch { }
      bool on = gameHotkeyBox != null && gameHotkeyBox.Checked;
      if (!on)
      {
        if (gameHotkeyButton != null) gameHotkeyButton.Text = "Клавиша выкл: " + HotkeyText();
        return;
      }
      uint mod = (uint)(gameMod | 0x4000);
      bool ok = RegisterHotKey(Handle, GameHotId, mod, (uint)gameKey);
      if (gameHotkeyButton != null) gameHotkeyButton.Text = ok ? "Клавиша: " + HotkeyText() : "Занята: " + HotkeyText();
    }

    string HotkeyText()
    {
      string text = "";
      if ((gameMod & 2) != 0) text += "Ctrl+";
      if ((gameMod & 4) != 0) text += "Shift+";
      if ((gameMod & 1) != 0) text += "Alt+";
      if (gameKey == Keys.Oem3) text += "Ё";
      else text += gameKey.ToString();
      return text;
    }

    void BeginGamePick(bool fromHotkey)
    {
      if (picking) return;
      picking = true;
      bool soft = gameSoftBox != null && gameSoftBox.Checked;
      bool restore = !soft && !fromHotkey && Visible;
      try
      {
        if (!soft) Hide();
        using (PickForm pick = new PickForm())
        {
          pick.Soft = soft;
          DialogResult picked = soft ? Pump(pick) : pick.ShowDialog();
          if (picked != DialogResult.OK)
          {
            if (restore) RestoreStudio();
            if (gameStatus != null) gameStatus.Text = "Отменила.";
            return;
          }
          Rectangle rect = pick.Chosen;
          try { pick.Hide(); } catch { }
          if (gameStatus != null) gameStatus.Text = "Читаю область…";
          Application.DoEvents();
          Thread.Sleep(80);
          Bitmap shot = new Bitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height), PixelFormat.Format32bppArgb);
          using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(rect.Location, Point.Empty, shot.Size);
          Thread worker = new Thread(new ThreadStart(delegate { FinishGame(shot, rect, soft); }));
          worker.IsBackground = true;
          worker.Name = "RechGame";
          worker.Start();
        }
      }
      catch (Exception ex)
      {
        if (restore) RestoreStudio();
        if (gameStatus != null) gameStatus.Text = ex.Message;
      }
      finally
      {
        picking = false;
      }
    }

    static DialogResult Pump(Form form)
    {
      form.Show();
      while (form.Visible)
      {
        Application.DoEvents();
        Thread.Sleep(15);
      }
      return form.DialogResult;
    }

    void FinishGame(Bitmap shot, Rectangle place, bool soft)
    {
      try
      {
        string raw = "";
        Exception boom = null;
        bool picture = false;
        bool translate = false;
        bool later = false;
        List<ShotPiece> pieces = null;
        Invoke(new Action(delegate
        {
          picture = gamePictureBox != null && gamePictureBox.Checked;
          translate = gameTranslateBox != null && gameTranslateBox.Checked;
          later = gameSpeakBox != null && gameSpeakBox.Checked;
          try
          {
            if (picture) pieces = GameOcr.Layout(shot);
            if (pieces != null && pieces.Count > 0) raw = GameOcr.JoinPieces(pieces, false);
            else raw = GameOcr.Read(shot);
          }
          catch (Exception ex) { boom = ex; }
        }));
        if (boom != null) throw boom;
        raw = Regex.Replace(raw == null ? "" : raw, "\\s+", " ").Trim();
        if (raw.Length == 0) throw new Exception("В рамке нет текста.");
        string spoken = raw;
        bool did = false;
        if (translate && GameOcr.MostlyLatin(raw))
        {
          Ui(delegate { if (gameStatus != null) gameStatus.Text = "Перевожу…"; });
          if (pieces != null && pieces.Count > 0)
          {
            int n;
            for (n = 0; n < pieces.Count; n++)
            {
              ShotPiece piece = pieces[n];
              piece.Ru = piece.Text;
              if (!GameOcr.MostlyLatin(piece.Text)) continue;
              try { piece.Ru = GameOcr.Translate(piece.Text); }
              catch { piece.Ru = piece.Text; }
            }
            spoken = GameOcr.JoinPieces(pieces, true);
          }
          else spoken = GameOcr.Translate(raw);
          did = true;
        }
        string shown = spoken;
        string aloud = ApplyNumbers(spoken, false, true);
        if (shown.Length > 1200) shown = shown.Substring(0, 1200);
        if (aloud.Length > 900) aloud = aloud.Substring(0, 900);
        Bitmap card = null;
        if (picture && pieces != null && pieces.Count > 0) card = GameOcr.Compose(shot, pieces, did);
        if (later || card != null)
        {
          bool go = false;
          Invoke(new Action(delegate
          {
            if (card != null)
            {
              using (ShotSlip slip = new ShotSlip(shot, card, aloud, did, place, pieces))
              {
                slip.Soft = soft;
                go = soft ? Pump(slip) == DialogResult.OK : slip.ShowDialog() == DialogResult.OK;
                if (go && slip.ToSay != null && slip.ToSay.Length > 0)
                {
                  shown = slip.ToSay;
                  aloud = ApplyNumbers(slip.ToSay, false, true);
                  if (aloud.Length > 900) aloud = aloud.Substring(0, 900);
                  did = true;
                  if (card != null) card.Dispose();
                  card = GameOcr.Compose(shot, pieces, true);
                }
              }
            }
            else
            {
              using (GameSlip slip = new GameSlip(raw, shown, did, place))
              {
                slip.Soft = soft;
                go = soft ? Pump(slip) == DialogResult.OK : slip.ShowDialog() == DialogResult.OK;
                if (go && slip.ToSay != null && slip.ToSay.Length > 0)
                {
                  shown = slip.VoiceText != null && slip.VoiceText.Length > 0 ? slip.VoiceText : slip.ToSay;
                  aloud = ApplyNumbers(slip.ToSay, false, true);
                  if (aloud.Length > 900) aloud = aloud.Substring(0, 900);
                }
              }
            }
          }));
          if (!go)
          {
            Ui(delegate { if (gameStatus != null) gameStatus.Text = "Оставила без озвучки."; });
            if (card != null) card.Dispose();
            return;
          }
        }
        Ui(delegate
        {
          bool drop = gameDeleteBox != null && gameDeleteBox.Checked;
          string keep = null;
          string shotPath = null;
          string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rech", "game");
          if (!drop)
          {
            Directory.CreateDirectory(dir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            keep = Path.Combine(dir, stamp + ".wav");
            if (card != null)
            {
              shotPath = Path.Combine(dir, stamp + ".png");
              try { card.Save(shotPath, ImageFormat.Png); }
              catch { shotPath = null; }
            }
          }
          if (card != null) card.Dispose();
          card = null;
          GameLine line = new GameLine();
          line.When = DateTime.Now.ToString("HH:mm");
          line.Original = raw;
          line.Spoken = shown;
          line.Path = keep;
          line.Shot = shotPath;
          gameList.Items.Add(line);
          gameList.SelectedIndex = gameList.Items.Count - 1;
          if (gameStatus != null) gameStatus.Text = did ? (shotPath != null ? "Перевела на картинке и читаю." : "Перевела и читаю.") : "Читаю.";
          string who = SileroSpeaker(sileroBox.SelectedIndex < 0 ? 0 : sileroBox.SelectedIndex);
          speaker.Say(aloud, true, who, false, keep);
        });
      }
      catch (Exception ex)
      {
        Ui(delegate { if (gameStatus != null) gameStatus.Text = ex.Message; });
      }
      finally
      {
        shot.Dispose();
      }
    }

    void ReplayGame()
    {
      GameLine line = gameList.SelectedItem as GameLine;
      if (line == null)
      {
        gameStatus.Text = "Выбери строку.";
        return;
      }
      if (line.Path != null && File.Exists(line.Path))
      {
        string path = line.Path;
        Thread worker = new Thread(new ThreadStart(delegate
        {
          try
          {
            using (SoundPlayer player = new SoundPlayer(path)) player.PlaySync();
          }
          catch (Exception ex)
          {
            Ui(delegate { gameStatus.Text = ex.Message; });
          }
        }));
        worker.IsBackground = true;
        worker.Start();
        gameStatus.Text = "Ещё раз, с записи.";
        return;
      }
      string who = SileroSpeaker(sileroBox.SelectedIndex < 0 ? 0 : sileroBox.SelectedIndex);
      speaker.Say(line.Spoken, true, who, false, null);
      gameStatus.Text = "Записи нет, читаю заново.";
    }

    void ClearGame()
    {
      foreach (object item in gameList.Items)
      {
        GameLine line = item as GameLine;
        if (line != null && line.Path != null)
        {
          try { if (File.Exists(line.Path)) File.Delete(line.Path); } catch { }
        }
        if (line != null && line.Shot != null)
        {
          try { if (File.Exists(line.Shot)) File.Delete(line.Shot); } catch { }
        }
      }
      gameList.Items.Clear();
      gameStatus.Text = "Список пуст.";
    }

    void DeleteGame()
    {
      int index = gameList.SelectedIndex;
      GameLine line = gameList.SelectedItem as GameLine;
      if (line == null)
      {
        gameStatus.Text = "Выбери строку.";
        return;
      }
      if (line.Path != null)
      {
        try { if (File.Exists(line.Path)) File.Delete(line.Path); } catch { }
      }
      if (line.Shot != null)
      {
        try { if (File.Exists(line.Shot)) File.Delete(line.Shot); } catch { }
      }
      gameList.Items.RemoveAt(index);
      if (gameList.Items.Count > 0) gameList.SelectedIndex = Math.Min(index, gameList.Items.Count - 1);
      gameStatus.Text = "Строку убрала.";
    }

    void ToggleEar()
    {
      if (earOn)
      {
        StopEar();
        if (earButton != null)
        {
          earButton.Enabled = true;
          earButton.Text = "Слушать звук";
        }
        if (gameStatus != null) gameStatus.Text = "Звук выключен.";
        return;
      }
      if (!SileroLocal.Ready)
      {
        gameStatus.Text = "Сначала скачай голос. Без него перевод нечего произнести.";
        return;
      }
      earButton.Enabled = false;
      earButton.Text = "Готовлю…";
      Thread prep = new Thread(new ThreadStart(delegate
      {
        try
        {
          EnsureEar(delegate(string text) { Ui(delegate { if (gameStatus != null) gameStatus.Text = text; }); });
          Ui(delegate { OpenEar(); });
        }
        catch (Exception ex)
        {
          Ui(delegate
          {
            if (earButton != null)
            {
              earButton.Enabled = true;
              earButton.Text = "Слушать звук";
            }
            if (gameStatus != null) gameStatus.Text = ex.Message;
          });
        }
      }));
      prep.IsBackground = true;
      prep.Name = "RechEarPrep";
      prep.Start();
    }

    static string EarDir()
    {
      return Path.Combine(SileroLocal.AsciiHome(), "ear");
    }

    static void EnsureEar(Action<string> status)
    {
      string dir = EarDir();
      Directory.CreateDirectory(dir);
      string exe = Path.Combine(dir, "whisper-cli.exe");
      if (!File.Exists(exe))
      {
        string zip = Path.Combine(dir, "whisper.zip");
        SileroLocal.Download("https://github.com/ggml-org/whisper.cpp/releases/download/b5454/whisper-bin-x64.zip", zip, status, "Распознавание");
        using (ZipArchive pack = ZipFile.OpenRead(zip))
        {
          foreach (ZipArchiveEntry entry in pack.Entries)
          {
            string name = Path.GetFileName(entry.FullName);
            if (name == null || name.Length == 0) continue;
            entry.ExtractToFile(Path.Combine(dir, name), true);
          }
        }
        try { File.Delete(zip); } catch { }
      }
      if (!File.Exists(exe)) throw new Exception("В архиве не нашёлся whisper-cli.exe.");
      string model = Path.Combine(dir, "ggml-base.en.bin");
      SileroLocal.Download("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin", model, status, "Модель");
      if (!File.Exists(model) || new FileInfo(model).Length < 100L * 1024L * 1024L) throw new Exception("Модель распознавания не скачалась.");
    }

    void OpenEar()
    {
      StopEar();
      earPhrases = new BlockingCollection<float[]>(8);
      earBuf = null;
      earTalk = false;
      earHot = false;
      earOn = true;
      earThread = new Thread(new ThreadStart(EarLoop));
      earThread.IsBackground = true;
      earThread.Name = "RechEar";
      earThread.Start();
      try
      {
        earCapture = new WasapiLoopbackCapture();
        earInRate = earCapture.WaveFormat.SampleRate;
        earCapture.DataAvailable += OnEarData;
        earCapture.StartRecording();
      }
      catch (Exception ex)
      {
        StopEar();
        if (earButton != null)
        {
          earButton.Enabled = true;
          earButton.Text = "Слушать звук";
        }
        gameStatus.Text = "Не вижу колонки. " + ex.Message;
        return;
      }
      earButton.Enabled = true;
      earButton.Text = "Стоп звук";
      gameStatus.Text = "Слушаю колонки. Английское скажу по-русски. Пока Люси говорит, запись молчит.";
    }

    void StopEar()
    {
      earOn = false;
      try
      {
        if (earCapture != null)
        {
          earCapture.DataAvailable -= OnEarData;
          earCapture.StopRecording();
          earCapture.Dispose();
        }
      }
      catch
      {
      }
      earCapture = null;
      try { if (earPhrases != null) earPhrases.CompleteAdding(); } catch { }
      lock (earGate)
      {
        earBuf = null;
        earTalk = false;
      }
    }

    void OnEarData(object sender, WaveInEventArgs e)
    {
      if (!earOn || e.BytesRecorded < 8 || earCapture == null) return;
      if (speaker != null && speaker.Speaking)
      {
        lock (earGate)
        {
          earBuf = null;
          earTalk = false;
          earHot = true;
        }
        return;
      }
      if (earHot)
      {
        earHot = false;
        earHold = DateTime.UtcNow.AddMilliseconds(250);
        lock (earGate) { earBuf = null; earTalk = false; }
        return;
      }
      if (DateTime.UtcNow < earHold) return;
      WaveFormat fmt = earCapture.WaveFormat;
      if (fmt.Encoding != WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample != 32 || fmt.Channels < 1) return;
      int channels = fmt.Channels;
      int frames = e.BytesRecorded / (4 * channels);
      if (frames < 1) return;
      float[] mono = new float[frames];
      double energy = 0;
      int i;
      for (i = 0; i < frames; i++)
      {
        float sum = 0;
        int c;
        for (c = 0; c < channels; c++) sum += BitConverter.ToSingle(e.Buffer, (i * channels + c) * 4);
        float sample = sum / channels;
        mono[i] = sample;
        energy += sample * sample;
      }
      double rms = Math.Sqrt(energy / frames);
      bool loud = rms > 0.012;
      int rate = fmt.SampleRate;
      lock (earGate)
      {
        if (!earTalk)
        {
          if (!loud) return;
          earTalk = true;
          earBuf = new List<float>();
        }
        earBuf.AddRange(mono);
        int cap = earPhraseSec;
        if (cap < 3) cap = 5;
        if (cap > 8) cap = 8;
        double pause = 0.7;
        bool ended = false;
        if (!loud)
        {
          int look = Math.Min(earBuf.Count, (int)(rate * pause));
          int n;
          double tail = 0;
          for (n = earBuf.Count - look; n < earBuf.Count; n++) tail += earBuf[n] * earBuf[n];
          bool quiet = look > 0 && Math.Sqrt(tail / look) < 0.01;
          ended = quiet && look >= (int)(rate * pause) && earBuf.Count > (int)(rate * 1.1);
        }
        if (earBuf.Count >= rate * cap) ended = true;
        if (!ended) return;
        float[] phrase = earBuf.ToArray();
        earBuf = null;
        earTalk = false;
        float[] small = Down16(phrase, rate);
        if (earPhrases != null && small.Length > 14000) earPhrases.TryAdd(small);
      }
    }

    static float[] Down16(float[] src, int from)
    {
      if (src == null || src.Length == 0) return new float[0];
      if (from <= 16000) return src;
      int n = (int)((long)src.Length * 16000L / from);
      if (n < 1) n = 1;
      float[] dst = new float[n];
      double step = (double)from / 16000.0;
      int i;
      for (i = 0; i < n; i++)
      {
        double pos = i * step;
        int a = (int)pos;
        if (a >= src.Length) a = src.Length - 1;
        int b = a + 1;
        if (b >= src.Length) b = src.Length - 1;
        double f = pos - a;
        dst[i] = (float)(src[a] * (1.0 - f) + src[b] * f);
      }
      return dst;
    }

    void EarLoop()
    {
      BlockingCollection<float[]> mine = earPhrases;
      if (mine == null) return;
      try
      {
        foreach (float[] samples in mine.GetConsumingEnumerable())
        {
          if (!earOn) break;
          try
          {
            Hear(samples);
          }
          catch (Exception ex)
          {
            Ui(delegate { if (gameStatus != null) gameStatus.Text = ex.Message; });
          }
        }
      }
      catch
      {
      }
    }

    void Hear(float[] samples)
    {
      string dir = EarDir();
      string wav = Path.Combine(dir, "phrase.wav");
      WritePcm(wav, samples);
      string heard = Whisper(wav);
      try { File.Delete(wav); } catch { }
      heard = StripCues(heard);
      if (heard.Length < 2 || EarJunk(heard) || !GameOcr.MostlyLatin(heard)) return;
      Ui(delegate { if (gameStatus != null) gameStatus.Text = "Услышала: " + heard; });
      string spoken = GameOcr.Translate(heard);
      spoken = ApplyNumbers(spoken, false, true);
      if (spoken.Length == 0) return;
      if (spoken.Length > 400) spoken = spoken.Substring(0, 400);
      string lineHeard = heard;
      Ui(delegate
      {
        GameLine line = new GameLine();
        line.When = DateTime.Now.ToString("HH:mm");
        line.Original = lineHeard;
        line.Spoken = spoken;
        gameList.Items.Add(line);
        gameList.SelectedIndex = gameList.Items.Count - 1;
        if (gameStatus != null) gameStatus.Text = lineHeard + " → " + spoken;
        string who = SileroSpeaker(sileroBox.SelectedIndex < 0 ? 0 : sileroBox.SelectedIndex);
        speaker.Say(spoken, true, who, false, null);
      });
    }

    static string StripCues(string text)
    {
      if (text == null) return "";
      string raw = text.Trim();
      if ((raw.StartsWith("(") && raw.EndsWith(")")) || (raw.StartsWith("[") && raw.EndsWith("]"))) return "";
      string clean = Regex.Replace(raw, "\\([^)]{0,80}\\)", " ");
      clean = Regex.Replace(clean, "\\[[^\\]]{0,80}\\]", " ");
      clean = Regex.Replace(clean, "[♪♫]+", " ");
      return Regex.Replace(clean, "\\s+", " ").Trim(' ', '.', ',', ';', '-', '—');
    }

    static bool EarJunk(string text)
    {
      string t = text.Trim().ToLowerInvariant();
      if (t.Length < 2) return true;
      if (t.IndexOf("miniaudio", StringComparison.Ordinal) >= 0) return true;
      if (t.IndexOf("decode", StringComparison.Ordinal) >= 0) return true;
      if (t.IndexOf("whisper", StringComparison.Ordinal) >= 0) return true;
      if (t.IndexOf("ggml", StringComparison.Ordinal) >= 0) return true;
      if (Regex.IsMatch(t, "^(?:[\\(\\[\\s♪♫]*)(?:upbeat|dramatic|soft|sad|tense|epic|background|ominous|gentle|loud|quiet|playing|continues|fades|fading|emotional|suspenseful|playful|cheerful|somber)?[\\s\\-]*?(?:music|applause|laughter|laughs|laughing|sighs|sigh|silence|inaudible|cheering|clapping|coughs|coughing|gasp|gasps|breathing|footsteps|thunder|ringtone)(?:[\\s\\]\\)\\.♪♫]*)$")) return true;
      string[] bad = new string[] {
        "thanks for watching", "thank you for watching", "thanks for watching.",
        "subscribe", "[music]", "[blank_audio]", "blank audio", "you", "bye.", "thank you.",
        "thank you", "thanks", "thanks.", "hello", "hello.", "hi", "hi.", "and", "the",
        "i", "oh", "hmm", "okay", "ok", "yeah", "yes", "no", "bye", "so", "well", "um", "uh",
        "careless", "the careless", "sorry", "i'm sorry", "subtitles"
      };
      int i;
      for (i = 0; i < bad.Length; i++) if (t == bad[i]) return true;
      return false;
    }

    static void WritePcm(string path, float[] samples)
    {
      using (FileStream file = File.Create(path))
      using (BinaryWriter writer = new BinaryWriter(file))
      {
        int data = samples.Length * 2;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + data);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16000);
        writer.Write(16000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(data);
        int i;
        for (i = 0; i < samples.Length; i++)
        {
          float s = samples[i];
          if (s > 1f) s = 1f;
          if (s < -1f) s = -1f;
          writer.Write((short)(s * 32767f));
        }
      }
    }

    static string Whisper(string wav)
    {
      string dir = EarDir();
      ProcessStartInfo info = new ProcessStartInfo();
      info.FileName = Path.Combine(dir, "whisper-cli.exe");
      info.WorkingDirectory = dir;
      info.Arguments = "-m \"" + Path.Combine(dir, "ggml-base.en.bin") + "\" -f \"" + wav + "\" -l en -nt -np -otxt -t 4 -bs 1 -bo 1 -nth 0.7 -et 2.0 -lpt -0.8 -tp 0 -tpi 0 -nf -sns";
      info.UseShellExecute = false;
      info.CreateNoWindow = true;
      info.RedirectStandardOutput = true;
      info.RedirectStandardError = true;
      Process process = Process.Start(info);
      if (process == null) throw new Exception("Распознавание не запустилось.");
      process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { };
      process.BeginErrorReadLine();
      string stdout = process.StandardOutput.ReadToEnd();
      if (!process.WaitForExit(25000))
      {
        try { process.Kill(true); } catch { }
        return "";
      }
      string txt = wav + ".txt";
      if (File.Exists(txt))
      {
        string file = "";
        try { file = File.ReadAllText(txt, Encoding.UTF8); } catch { }
        try { File.Delete(txt); } catch { }
        if (file.Trim().Length > 0) return WhisperText(file);
      }
      return WhisperText(stdout);
    }

    static string WhisperText(string raw)
    {
      if (raw == null) return "";
      StringBuilder text = new StringBuilder();
      string[] lines = raw.Split('\n');
      int i;
      for (i = 0; i < lines.Length; i++)
      {
        string line = lines[i].Trim();
        if (line.Length == 0) continue;
        string low = line.ToLowerInvariant();
        if (low.IndexOf("miniaudio", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("decode", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("whisper", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("ggml", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("system_info", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("load time", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("reading audio", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf("error:", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf(".wav", StringComparison.Ordinal) >= 0) continue;
        if (low.IndexOf(".bin", StringComparison.Ordinal) >= 0) continue;
        if (low.StartsWith("main:", StringComparison.Ordinal)) continue;
        if (line.StartsWith("["))
        {
          int end = line.IndexOf(']');
          if (end > 0 && end < 28) line = line.Substring(end + 1).Trim();
        }
        if (line.Length == 0) continue;
        text.Append(line);
        text.Append(' ');
      }
      return text.ToString().Trim();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "SendMessageW")]
    static extern IntPtr SendMessageCue(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
  }

  public class RewardPrefs
  {
    public string ClientId;
    public string Token;
    public string Refresh;
    public string SongTitle;
    public string LucyTitle;
    public int Seconds;
    public int Volume;
    public bool Youtube;
    public bool Rutube;
    public bool Vk;
    public string Facts;
    public string BotToken;
    public string BotRefresh;
    public string BotLogin;
    public string Channel;
  }

  public class SongItem
  {
    public string Id;
    public string Embed;
    public string By;
    public string Host;
  }

  public class Rewards
  {
    static readonly object gate = new object();
    static RewardPrefs prefs = new RewardPrefs();
    static List<SongItem> queue = new List<SongItem>();
    static SongItem now;
    static DateTime until;
    static int generation;
    static int started;
    static HashSet<string> seen = new HashSet<string>();
    static Dictionary<string, DateTime> cool = new Dictionary<string, DateTime>();
    public static string Status = "Награды Twitch выключены.";
    public static string UserCode = "";
    public static event Action<string, string, bool> Line;
    public static event Action<string, string> TokenReady;
    public static event Action<string, string, string> BotReady;
    public static string BotStatus = "В чат Twitch ещё не входит.";
    static string botToken = "";
    static string botRefresh = "";
    static string botUserId = "";
    static string botLogin = "";
    static string channelName = "";
    static string broadLogin = "";
    static string broadId = "";
    static string lastLine = "";
    static DateTime lastAt;
    static string lastEmbed = "";
    static string lastHost = "";
    static bool concealed;

    public static void Apply(RewardPrefs next)
    {
      if (next == null) next = new RewardPrefs();
      lock (gate)
      {
        bool restart = next.ClientId != prefs.ClientId || next.Token != prefs.Token;
        prefs = next;
        botToken = next.BotToken == null ? "" : next.BotToken;
        botRefresh = next.BotRefresh == null ? "" : next.BotRefresh;
        botLogin = next.BotLogin == null ? "" : next.BotLogin;
        channelName = next.Channel == null ? "" : next.Channel;
        if (restart) generation = generation + 1;
      }
      Start();
    }

    public static void Start()
    {
      if (Interlocked.Exchange(ref started, 1) == 1) return;
      Thread thread = new Thread(Loop);
      thread.IsBackground = true;
      thread.Name = "TwitchRewards";
      thread.Start();
    }

    public static void BeginLogin(string clientId)
    {
      clientId = clientId == null ? "" : clientId.Trim();
      if (clientId.Length < 10)
      {
        Status = "Нужен Client ID с dev.twitch.tv. Секрет приложения не нужен.";
        return;
      }
      Thread thread = new Thread(delegate() { Login(clientId, false); });
      thread.IsBackground = true;
      thread.Name = "TwitchLogin";
      thread.Start();
    }

    public static void BeginBotLogin(string clientId)
    {
      clientId = clientId == null ? "" : clientId.Trim();
      if (clientId.Length < 10)
      {
        BotStatus = "Нужен тот же Client ID. Секрет не нужен.";
        return;
      }
      Thread thread = new Thread(delegate() { Login(clientId, true); });
      thread.IsBackground = true;
      thread.Name = "LucyLogin";
      thread.Start();
    }

    static void Login(string clientId, bool bot)
    {
      Action<string> say = delegate(string text)
      {
        if (bot) BotStatus = text;
        else Status = text;
      };
      try
      {
        say("Прошу код у Twitch…");
        UserCode = "";
        string scopes = bot ? "user:write:chat" : "channel:read:redemptions channel:manage:redemptions";
        string body = "client_id=" + Uri.EscapeDataString(clientId) + "&scopes=" + Uri.EscapeDataString(scopes);
        string json = PostForm("https://id.twitch.tv/oauth2/device", body);
        JObject root = JObject.Parse(json);
        string device = (string)root["device_code"];
        string code = (string)root["user_code"];
        int interval = root["interval"] == null ? 5 : (int)root["interval"];
        if (interval < 2) interval = 5;
        UserCode = code;
        say(bot ? "Код " + code + ". Войди на Twitch аккаунтом Lucy и открой twitch.tv/activate." : "Код " + code + ". Открой twitch.tv/activate и подтверди награды.");
        try
        {
          Process.Start(new ProcessStartInfo("https://www.twitch.tv/activate") { UseShellExecute = true });
        }
        catch
        {
        }
        DateTime deadline = DateTime.UtcNow.AddMinutes(15);
        while (DateTime.UtcNow < deadline)
        {
          Thread.Sleep(interval * 1000);
          string ask = "client_id=" + Uri.EscapeDataString(clientId) + "&device_code=" + Uri.EscapeDataString(device) + "&grant_type=" + Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:device_code");
          string tokenJson;
          try
          {
            tokenJson = PostForm("https://id.twitch.tv/oauth2/token", ask);
          }
          catch (Exception ex)
          {
            string note = ex.Message == null ? "" : ex.Message;
            if (note.IndexOf("authorization_pending", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (note.IndexOf("slow_down", StringComparison.OrdinalIgnoreCase) >= 0)
            {
              interval = interval + 2;
              continue;
            }
            throw;
          }
          JObject token = JObject.Parse(tokenJson);
          string access = (string)token["access_token"];
          string refresh = (string)token["refresh_token"];
          if (access == null || access.Length == 0) throw new Exception("Twitch не отдал ключ.");
          if (bot)
          {
            string login = "";
            string id = "";
            try
            {
              JObject who = JObject.Parse(GetAuthed("https://api.twitch.tv/helix/users", access, clientId));
              login = (string)who.SelectToken("data[0].login");
              id = (string)who.SelectToken("data[0].id");
            }
            catch
            {
            }
            lock (gate)
            {
              botToken = access;
              botRefresh = refresh == null ? "" : refresh;
              botLogin = login == null ? "" : login;
              botUserId = id == null ? "" : id;
            }
            UserCode = "";
            say(login == null || login.Length == 0 ? "Вход есть. Сделай аккаунт модератором канала." : "В чат пишет " + login + ". Сделай этот аккаунт модератором.");
            if (BotReady != null) BotReady(access, refresh, login);
            return;
          }
          lock (gate)
          {
            prefs.ClientId = clientId;
            prefs.Token = access;
            prefs.Refresh = refresh;
            generation = generation + 1;
          }
          UserCode = "";
          Status = "Twitch подтвердил. Слушаю награды.";
          if (TokenReady != null) TokenReady(access, refresh);
          return;
        }
        say("Код истёк. Нажми вход ещё раз.");
      }
      catch (Exception ex)
      {
        UserCode = "";
        say("Вход не вышел: " + (ex.Message == null ? "ошибка Twitch" : ex.Message));
      }
    }

    public static bool IsOwn(string author)
    {
      if (author == null || botLogin == null || botLogin.Length == 0) return false;
      return string.Equals(author.Trim(), botLogin, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Echo(string author, string text, string platform)
    {
      if (platform == "lucy") return false;
      if (IsOwn(author)) return true;
      string heard = text == null ? "" : text.Trim();
      if (heard.Length == 0) return false;
      lock (gate)
      {
        if (lastLine.Length == 0 || !string.Equals(heard, lastLine, StringComparison.Ordinal)) return false;
        return (DateTime.UtcNow - lastAt).TotalSeconds < 40;
      }
    }

    static void Remember(string text)
    {
      if (text == null) return;
      text = text.Trim();
      if (text.Length == 0) return;
      lock (gate)
      {
        lastLine = text;
        lastAt = DateTime.UtcNow;
      }
    }

    public static string PostToTwitch(string text)
    {
      if (text == null) return "";
      text = Regex.Replace(text, "\\s+", " ").Trim();
      if (text.Length == 0) return "";
      if (text.Length > 450) text = text.Substring(0, 450).Trim();
      Remember(text);
      string token;
      string refresh;
      string client;
      string channel;
      string sender;
      lock (gate)
      {
        token = botToken;
        refresh = botRefresh;
        client = prefs.ClientId;
        channel = channelName;
        sender = botUserId;
      }
      if (token == null || token.Length == 0) return "В Twitch не написала: войди аккаунтом Lucy.";
      if (channel == null || channel.Length == 0) return "В Twitch не написала: слева подключи свой канал Twitch.";
      if (client == null || client.Length == 0) return "В Twitch не написала: нет Client ID.";
      try
      {
        return SendChat(text, token, refresh, client, channel, sender, true);
      }
      catch (Exception ex)
      {
        string note = ex.Message == null ? "ошибка" : ex.Message;
        if (note.Length > 140) note = note.Substring(0, 140);
        return "В Twitch не написала: " + note;
      }
    }

    static string SendChat(string text, string token, string refresh, string client, string channel, string sender, bool allowRefresh)
    {
      if (sender == null || sender.Length == 0)
      {
        JObject me = JObject.Parse(GetAuthed("https://api.twitch.tv/helix/users", token, client));
        sender = (string)me.SelectToken("data[0].id");
        string login = (string)me.SelectToken("data[0].login");
        lock (gate)
        {
          botUserId = sender == null ? "" : sender;
          if (login != null && login.Length > 0) botLogin = login;
        }
      }
      string broad = Broad(token, client, channel);
      string body = "{\"broadcaster_id\":\"" + broad + "\",\"sender_id\":\"" + sender + "\",\"message\":" + JsonConvert.ToString(text) + "}";
      string json;
      try
      {
        json = PostAuthed("https://api.twitch.tv/helix/chat/messages", body, token, client);
      }
      catch (Exception ex)
      {
        string note = ex.Message == null ? "" : ex.Message;
        if (allowRefresh && note.StartsWith("401", StringComparison.Ordinal))
        {
          string next = RefreshBot(client, refresh);
          if (next.Length == 0) throw;
          return SendChat(text, next, refresh, client, channel, sender, false);
        }
        throw;
      }
      JObject root = JObject.Parse(json);
      JToken sent = root.SelectToken("data[0].is_sent");
      if (sent != null && sent.Type == JTokenType.Boolean && (bool)sent)
      {
        BotStatus = "Написала в чат Twitch.";
        return "";
      }
      string why = (string)root.SelectToken("data[0].drop_reason.message");
      if (why == null || why.Length == 0) why = (string)root.SelectToken("data[0].drop_reason.code");
      if (why == null) why = "не пустил";
      if (why.IndexOf("moderat", StringComparison.OrdinalIgnoreCase) >= 0) return "Twitch не пустил. Сделай аккаунт Lucy модератором канала.";
      return "Twitch не пустил: " + why;
    }

    static string Broad(string token, string client, string channel)
    {
      lock (gate)
      {
        if (broadLogin == channel && broadId != null && broadId.Length > 0) return broadId;
      }
      JObject who = JObject.Parse(GetAuthed("https://api.twitch.tv/helix/users?login=" + Uri.EscapeDataString(channel), token, client));
      string id = (string)who.SelectToken("data[0].id");
      if (id == null || id.Length == 0) throw new Exception("канал Twitch не найден");
      lock (gate)
      {
        broadLogin = channel;
        broadId = id;
      }
      return id;
    }

    static string RefreshBot(string client, string refresh)
    {
      if (client == null || refresh == null || refresh.Length == 0) return "";
      try
      {
        string ask = "client_id=" + Uri.EscapeDataString(client) + "&refresh_token=" + Uri.EscapeDataString(refresh) + "&grant_type=refresh_token";
        JObject token = JObject.Parse(PostForm("https://id.twitch.tv/oauth2/token", ask));
        string access = (string)token["access_token"];
        string next = (string)token["refresh_token"];
        if (access == null || access.Length == 0) return "";
        lock (gate)
        {
          botToken = access;
          if (next != null && next.Length > 0) botRefresh = next;
        }
        if (BotReady != null) BotReady(access, next == null || next.Length == 0 ? refresh : next, botLogin);
        return access;
      }
      catch
      {
        return "";
      }
    }

    public static void Conceal(bool on)
    {
      lock (gate)
      {
        concealed = on;
        if (on) now = null;
        else if (now == null) NextLocked();
      }
    }

    public static bool Concealed
    {
      get { lock (gate) { return concealed; } }
    }

    public static bool Replay()
    {
      lock (gate)
      {
        if (lastEmbed == null || lastEmbed.Length == 0) return false;
        concealed = false;
        SongItem item = new SongItem();
        item.Id = Guid.NewGuid().ToString("N");
        item.Embed = lastEmbed;
        item.By = "проверка";
        item.Host = lastHost == null || lastHost.Length == 0 ? "YouTube" : lastHost;
        now = item;
        int seconds = prefs.Seconds;
        if (seconds < 20) seconds = 75;
        if (seconds > 180) seconds = 180;
        until = DateTime.UtcNow.AddSeconds(seconds);
        return true;
      }
    }

    public static void Skip()
    {
      lock (gate)
      {
        now = null;
        until = DateTime.UtcNow;
        NextLocked();
      }
    }

    public static void Tick()
    {
      lock (gate)
      {
        if (concealed) return;
        if (now != null && DateTime.UtcNow >= until)
        {
          now = null;
          NextLocked();
        }
      }
    }

    public static string NowText()
    {
      lock (gate)
      {
        if (concealed) return "Плеер скрыт, клип не играет.";
        if (now == null)
        {
          if (queue.Count == 0) return "Сейчас тишина.";
          return "В очереди " + queue.Count.ToString(CultureInfo.InvariantCulture) + ".";
        }
        int left = (int)(until - DateTime.UtcNow).TotalSeconds;
        if (left < 0) left = 0;
        return now.By + " · " + now.Host + " · ещё " + left.ToString(CultureInfo.InvariantCulture) + " с · в очереди " + queue.Count.ToString(CultureInfo.InvariantCulture);
      }
    }

    public static string CurrentEmbed()
    {
      lock (gate)
      {
        if (concealed || now == null || now.Embed == null) return "";
        return now.Embed;
      }
    }

    public static string NowJson()
    {
      lock (gate)
      {
        StringBuilder sb = new StringBuilder();
        bool quiet = concealed || now == null;
        sb.Append("{\"id\":").Append(JsonConvert.ToString(quiet ? "" : now.Id));
        sb.Append(",\"embed\":").Append(JsonConvert.ToString(quiet ? "" : now.Embed));
        sb.Append(",\"by\":").Append(JsonConvert.ToString(quiet ? "" : now.By));
        sb.Append(",\"host\":").Append(JsonConvert.ToString(quiet ? "" : now.Host));
        sb.Append(",\"hide\":").Append(concealed ? "true" : "false");
        int left = 0;
        if (now != null)
        {
          left = (int)(until - DateTime.UtcNow).TotalSeconds;
          if (left < 0) left = 0;
        }
        sb.Append(",\"left\":").Append(left.ToString(CultureInfo.InvariantCulture));
        int volume = prefs.Volume;
        if (volume < 0) volume = 0;
        if (volume > 100) volume = 100;
        sb.Append(",\"volume\":").Append(volume.ToString(CultureInfo.InvariantCulture));
        sb.Append('}');
        return sb.ToString();
      }
    }

    static void NextLocked()
    {
      if (queue.Count == 0)
      {
        now = null;
        return;
      }
      now = queue[0];
      queue.RemoveAt(0);
      int seconds = prefs.Seconds;
      if (seconds < 20) seconds = 75;
      if (seconds > 180) seconds = 180;
      until = DateTime.UtcNow.AddSeconds(seconds);
    }

    static void Loop()
    {
      while (true)
      {
        RewardPrefs current;
        int gen;
        lock (gate)
        {
          current = prefs;
          gen = generation;
        }
        if (current.ClientId == null || current.ClientId.Length < 10 || current.Token == null || current.Token.Length < 10)
        {
          if (UserCode == null || UserCode.Length == 0) Status = "Награды ждут вход через Twitch.";
          Thread.Sleep(1500);
          continue;
        }
        WsClient ws = null;
        try
        {
          ws = new WsClient();
          ws.Connect("wss://eventsub.wss.twitch.tv/ws", CancellationToken.None, null).GetAwaiter().GetResult();
          string session = "";
          bool subscribed = false;
          while (true)
          {
            lock (gate)
            {
              if (generation != gen) throw new Exception("настройки сменились");
            }
            string raw = ws.Receive(CancellationToken.None).GetAwaiter().GetResult();
            JObject root = JObject.Parse(raw);
            string type = (string)root.SelectToken("metadata.message_type");
            if (type == "session_keepalive") continue;
            if (type == "session_reconnect") throw new Exception("Twitch перенёс соединение");
            if (type == "revocation")
            {
              Status = "Twitch закрыл награды. Войди ещё раз.";
              throw new Exception("revocation");
            }
            if (type == "session_welcome")
            {
              session = (string)root.SelectToken("payload.session.id");
              if (!subscribed)
              {
                Subscribe(current, session);
                subscribed = true;
                Status = "Слушаю награды Twitch.";
              }
              continue;
            }
            if (type == "notification")
            {
              JToken ev = root.SelectToken("payload.event");
              if (ev != null) Accept(current, ev);
            }
          }
        }
        catch (Exception ex)
        {
          string note = ex.Message == null ? "" : ex.Message;
          if (note.IndexOf("401", StringComparison.Ordinal) >= 0)
          {
            if (!Refresh(current)) Status = "Ключ Twitch устарел. Войди ещё раз.";
          }
          else if (note != "настройки сменились" && note != "revocation")
          {
            Status = "Награды переподключаются.";
          }
          try { if (ws != null) ws.Abort(); } catch { }
          Thread.Sleep(4000);
        }
      }
    }

    static void Subscribe(RewardPrefs current, string session)
    {
      string me = GetString("https://api.twitch.tv/helix/users", current);
      JObject who = JObject.Parse(me);
      string id = (string)who.SelectToken("data[0].id");
      if (id == null || id.Length == 0) throw new Exception("Twitch не сказал, чей это канал.");
      JObject body = new JObject();
      body["type"] = "channel.channel_points_custom_reward_redemption.add";
      body["version"] = "1";
      body["condition"] = new JObject();
      body["condition"]["broadcaster_user_id"] = id;
      body["transport"] = new JObject();
      body["transport"]["method"] = "websocket";
      body["transport"]["session_id"] = session;
      PostJson("https://api.twitch.tv/helix/eventsub/subscriptions", body.ToString(Formatting.None), current);
    }

    static bool Refresh(RewardPrefs current)
    {
      if (current.Refresh == null || current.Refresh.Length == 0 || current.ClientId == null) return false;
      try
      {
        string ask = "client_id=" + Uri.EscapeDataString(current.ClientId) + "&refresh_token=" + Uri.EscapeDataString(current.Refresh) + "&grant_type=refresh_token";
        string json = PostForm("https://id.twitch.tv/oauth2/token", ask);
        JObject token = JObject.Parse(json);
        string access = (string)token["access_token"];
        string refresh = (string)token["refresh_token"];
        if (access == null || access.Length == 0) return false;
        lock (gate)
        {
          prefs.Token = access;
          if (refresh != null && refresh.Length > 0) prefs.Refresh = refresh;
          current.Token = access;
          if (refresh != null && refresh.Length > 0) current.Refresh = refresh;
          generation = generation + 1;
        }
        if (TokenReady != null) TokenReady(access, refresh == null || refresh.Length == 0 ? current.Refresh : refresh);
        Status = "Ключ обновился. Слушаю награды.";
        return true;
      }
      catch
      {
        return false;
      }
    }

    static void Accept(RewardPrefs current, JToken ev)
    {
      string redemption = (string)ev["id"];
      string user = (string)ev["user_name"];
      string input = (string)ev["user_input"];
      string title = (string)ev.SelectToken("reward.title");
      string rewardId = (string)ev.SelectToken("reward.id");
      string broadcaster = (string)ev["broadcaster_user_id"];
      if (user == null || user.Length == 0) user = "зритель";
      if (input == null) input = "";
      input = input.Trim();
      if (redemption != null)
      {
        lock (gate)
        {
          if (seen.Contains(redemption)) return;
          seen.Add(redemption);
          if (seen.Count > 80) seen.Clear();
        }
      }
      string songTitle = current.SongTitle == null || current.SongTitle.Trim().Length == 0 ? "Заказать песню" : current.SongTitle.Trim();
      string lucyTitle = current.LucyTitle == null || current.LucyTitle.Trim().Length == 0 ? "Поговорить с Люси" : current.LucyTitle.Trim();
      if (SameTitle(title, songTitle))
      {
        string embed;
        string host;
        string why;
        if (!SongLink(input, current, out embed, out host, out why))
        {
          Cancel(current, broadcaster, rewardId, redemption);
          Tell("Люси", user + ", " + why, false);
          return;
        }
        if (Cool(user, "song"))
        {
          Cancel(current, broadcaster, rewardId, redemption);
          Tell("Люси", user + ", погоди немного перед следующей песней.", false);
          return;
        }
        bool full;
        lock (gate)
        {
          full = queue.Count >= 6;
          if (!full)
          {
            SongItem item = new SongItem();
            item.Id = redemption == null ? Guid.NewGuid().ToString("N") : redemption;
            item.Embed = embed;
            item.By = user;
            item.Host = host;
            lastEmbed = embed;
            lastHost = host;
            queue.Add(item);
            concealed = false;
            if (now == null) NextLocked();
          }
        }
        if (full)
        {
          Cancel(current, broadcaster, rewardId, redemption);
          Tell("Люси", user + ", очередь полная. Подожди, пока доиграет.", false);
          return;
        }
        Tell("Люси", user + ", песня в очереди.", false);
        return;
      }
      if (SameTitle(title, lucyTitle))
      {
        if (input.Length == 0)
        {
          Cancel(current, broadcaster, rewardId, redemption);
          Tell("Люси", user + ", напиши вопрос в награде.", false);
          return;
        }
        if (!LucyLocal.Ready)
        {
          Cancel(current, broadcaster, rewardId, redemption);
          Tell("Люси", "Меня ещё не скачали, спросить нечем.", false);
          return;
        }
        if (Cool(user, "lucy"))
        {
          Cancel(current, broadcaster, rewardId, redemption);
          Tell("Люси", user + ", я ещё отвечаю. Спроси чуть позже.", false);
          return;
        }
        string facts;
        lock (gate) facts = prefs.Facts;
        Thread talk = new Thread(delegate()
        {
          string answer = "";
          try
          {
            answer = LucyLocal.Reply(user + ": " + input, facts);
            if (answer != null && answer.Length > 0) LucyLore.Remember(user + ": " + input, answer);
          }
          catch
          {
          }
          if (answer == null || answer.Length == 0) answer = "Не расслышала. Спроси ещё раз короче.";
          Tell("Люси", answer, true);
        });
        talk.IsBackground = true;
        talk.Name = "LucyReward";
        talk.Start();
      }
    }

    static bool Cool(string user, string kind)
    {
      string key = kind + ":" + (user == null ? "" : user.ToLowerInvariant());
      lock (gate)
      {
        DateTime last;
        if (cool.TryGetValue(key, out last) && (DateTime.UtcNow - last).TotalSeconds < 25) return true;
        cool[key] = DateTime.UtcNow;
        return false;
      }
    }

    static bool SameTitle(string title, string want)
    {
      if (title == null || want == null) return false;
      return string.Equals(title.Trim(), want.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool SongLink(string text, RewardPrefs current, out string embed, out string host, out string why)
    {
      embed = "";
      host = "";
      why = "нужна одна ссылка на песню с YouTube, Rutube или VK.";
      if (text == null) return false;
      Match yt = Regex.Match(text, "(?:youtube\\.com/(?:watch\\?v=|shorts/|embed/)|youtu\\.be/)([A-Za-z0-9_-]{6,16})", RegexOptions.IgnoreCase);
      if (yt.Success)
      {
        if (current != null && !current.Youtube)
        {
          why = "YouTube в настройках выключен.";
          return false;
        }
        embed = "https://www.youtube.com/embed/" + yt.Groups[1].Value + "?autoplay=1&rel=0&modestbranding=1&playsinline=1";
        host = "YouTube";
        return true;
      }
      Match ru = Regex.Match(text, "rutube\\.ru/(?:video|play/embed|shorts)/([a-f0-9]{16,40})", RegexOptions.IgnoreCase);
      if (ru.Success)
      {
        if (current != null && !current.Rutube)
        {
          why = "Rutube в настройках выключен.";
          return false;
        }
        embed = "https://rutube.ru/play/embed/" + ru.Groups[1].Value + "?autoplay=1";
        host = "Rutube";
        return true;
      }
      Match vk = Regex.Match(text, "(?:vk\\.com|vkvideo\\.ru)/video(-?\\d+)_(\\d+)", RegexOptions.IgnoreCase);
      if (vk.Success)
      {
        if (current != null && !current.Vk)
        {
          why = "VK Видео в настройках выключен.";
          return false;
        }
        embed = "https://vk.com/video_ext.php?oid=" + vk.Groups[1].Value + "&id=" + vk.Groups[2].Value + "&hd=2&autoplay=1";
        host = "VK Видео";
        return true;
      }
      return false;
    }

    static void Cancel(RewardPrefs current, string broadcaster, string rewardId, string redemption)
    {
      if (current == null || broadcaster == null || rewardId == null || redemption == null) return;
      try
      {
        string url = "https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions?broadcaster_id=" + Uri.EscapeDataString(broadcaster) + "&reward_id=" + Uri.EscapeDataString(rewardId) + "&id=" + Uri.EscapeDataString(redemption);
        HttpRequestMessage req = new HttpRequestMessage(new HttpMethod("PATCH"), url);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + current.Token);
        req.Headers.TryAddWithoutValidation("Client-Id", current.ClientId);
        req.Content = new StringContent("{\"status\":\"CANCELED\"}", Encoding.UTF8, "application/json");
        HttpResponseMessage res = Net.Http.Send(req);
        res.Dispose();
      }
      catch
      {
      }
    }

    static void Tell(string author, string text, bool speak)
    {
      Action<string, string, bool> handler = Line;
      if (handler != null) handler(author, text, speak);
    }

    static string PostForm(string url, string body)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
      req.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
      return Send(req);
    }

    static string PostAuthed(string url, string body, string token, string clientId)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
      req.Content = new StringContent(body, Encoding.UTF8, "application/json");
      req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
      req.Headers.TryAddWithoutValidation("Client-Id", clientId);
      return Send(req);
    }

    static string GetAuthed(string url, string token, string clientId)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
      req.Headers.TryAddWithoutValidation("Client-Id", clientId);
      return Send(req);
    }

    static string PostJson(string url, string body, RewardPrefs current)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, url);
      req.Content = new StringContent(body, Encoding.UTF8, "application/json");
      req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + current.Token);
      req.Headers.TryAddWithoutValidation("Client-Id", current.ClientId);
      return Send(req);
    }

    static string GetString(string url, RewardPrefs current)
    {
      HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
      req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + current.Token);
      req.Headers.TryAddWithoutValidation("Client-Id", current.ClientId);
      return Send(req);
    }

    static string Send(HttpRequestMessage req)
    {
      HttpResponseMessage res = Net.Http.Send(req);
      string text = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
      int code = (int)res.StatusCode;
      res.Dispose();
      if (code < 200 || code >= 300) throw new Exception(code.ToString(CultureInfo.InvariantCulture) + " " + (text == null ? "" : text));
      return text == null ? "" : text;
    }
  }

  public class OverlayLine
  {
    public string Author;
    public string Text;
    public string Mark;
    public string Time;
    public string Icons;
    public long Born;
  }

  public class OverlayStyle
  {
    public string Font = "Segoe UI";
    public int Size = 32;
    public bool Bold = true;
    public string Color = "#F4EFE6";
    public string Nick = "#FF8A3D";
    public string Stroke = "shadow";
    public bool Plate;
    public string Align = "bottom";
    public int Lines = 8;
    public int Hold = 8;
    public bool Platform = true;
    public bool Clock;
  }

  public class OverlayHub
  {
    readonly object gate = new object();
    readonly List<OverlayLine> lines = new List<OverlayLine>();
    OverlayStyle style = new OverlayStyle();
    int revision;
    TcpListener listener;
    volatile bool running;
    public int Port;

    public void SetStyle(OverlayStyle next)
    {
      if (next == null) return;
      lock (gate)
      {
        style = next;
        revision = revision + 1;
      }
    }

    public void Replace(List<OverlayLine> next)
    {
      lock (gate)
      {
        long now = NowMs();
        List<OverlayLine> kept = new List<OverlayLine>();
        int prev = 0;
        if (next != null)
        {
          int i;
          for (i = 0; i < next.Count; i++)
          {
            OverlayLine item = next[i];
            long born = now;
            int k;
            for (k = prev; k < lines.Count; k++)
            {
              if (Same(lines[k], item))
              {
                born = lines[k].Born;
                prev = k + 1;
                break;
              }
            }
            item.Born = born;
            kept.Add(item);
          }
        }
        while (kept.Count > 400) kept.RemoveAt(0);
        lines.Clear();
        int n;
        for (n = 0; n < kept.Count; n++) lines.Add(kept[n]);
        revision = revision + 1;
      }
    }

    static bool Same(OverlayLine a, OverlayLine b)
    {
      if (a == null || b == null) return false;
      return a.Author == b.Author && a.Text == b.Text && a.Mark == b.Mark && a.Time == b.Time;
    }

    public static long NowMs()
    {
      return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
    }

    public int Revision
    {
      get
      {
        lock (gate) return revision;
      }
    }

    public void Snapshot(List<OverlayLine> into, out OverlayStyle current)
    {
      lock (gate)
      {
        current = style;
        into.Clear();
        int i;
        for (i = 0; i < lines.Count; i++) into.Add(lines[i]);
      }
    }

    public void Start()
    {
      int port = 49135;
      int attempt;
      for (attempt = 0; attempt < 40; attempt++)
      {
        TcpListener probe = null;
        try
        {
          probe = new TcpListener(IPAddress.Loopback, port);
          probe.Start();
          listener = probe;
          Port = port;
          running = true;
          Thread thread = new Thread(AcceptLoop);
          thread.IsBackground = true;
          thread.Name = "RechOverlay";
          thread.Start();
          return;
        }
        catch
        {
          try { if (probe != null) probe.Stop(); } catch { }
          port = port + 1;
        }
      }
      Port = 0;
    }

    public void Stop()
    {
      running = false;
      try { if (listener != null) listener.Stop(); } catch { }
    }

    void AcceptLoop()
    {
      while (running)
      {
        TcpClient client = null;
        try
        {
          client = listener.AcceptTcpClient();
        }
        catch
        {
          if (!running) break;
          continue;
        }
        ThreadPool.QueueUserWorkItem(Handle, client);
      }
    }

    void Handle(object state)
    {
      TcpClient client = (TcpClient)state;
      try
      {
        client.ReceiveTimeout = 2000;
        client.SendTimeout = 2000;
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[4096];
        int read = 0;
        while (read < buffer.Length)
        {
          int got = stream.Read(buffer, read, buffer.Length - read);
          if (got <= 0) break;
          read = read + got;
          string sofar = Encoding.ASCII.GetString(buffer, 0, read);
          if (sofar.IndexOf("\r\n\r\n") >= 0) break;
        }
        string request = Encoding.ASCII.GetString(buffer, 0, read);
        string path = "/";
        int space = request.IndexOf(' ');
        if (space >= 0)
        {
          int next = request.IndexOf(' ', space + 1);
          if (next > space) path = request.Substring(space + 1, next - space - 1);
        }
        int query = path.IndexOf('?');
        if (query >= 0) path = path.Substring(0, query);
        byte[] body;
        string type;
        if (path == "/player/now")
        {
          body = Encoding.UTF8.GetBytes(Rewards.NowJson());
          type = "application/json; charset=utf-8";
        }
        else if (path == "/player/done")
        {
          Rewards.Skip();
          body = Encoding.UTF8.GetBytes("ok");
          type = "text/plain; charset=utf-8";
        }
        else if (path == "/player/off")
        {
          body = Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=\"utf-8\"><style>html,body{margin:0;background:#000}</style></head><body></body></html>");
          type = "text/html; charset=utf-8";
        }
        else if (path == "/player")
        {
          body = Encoding.UTF8.GetBytes(PlayerPage());
          type = "text/html; charset=utf-8";
        }
        else if (path == "/feed")
        {
          List<OverlayLine> copy = new List<OverlayLine>();
          OverlayStyle current;
          Snapshot(copy, out current);
          if (current == null) current = new OverlayStyle();
          body = Encoding.UTF8.GetBytes(FeedJsonText(current, copy));
          type = "application/json; charset=utf-8";
        }
        else if (path == "/" || path == "/overlay")
        {
          body = Encoding.UTF8.GetBytes(Page());
          type = "text/html; charset=utf-8";
        }
        else
        {
          body = Encoding.UTF8.GetBytes("no");
          type = "text/plain; charset=utf-8";
        }
        StringBuilder header = new StringBuilder();
        header.Append("HTTP/1.1 200 OK\r\n");
        header.Append("Content-Type: ");
        header.Append(type);
        header.Append("\r\nContent-Length: ");
        header.Append(body.Length.ToString(CultureInfo.InvariantCulture));
        header.Append("\r\nCache-Control: no-store\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n");
        byte[] head = Encoding.ASCII.GetBytes(header.ToString());
        stream.Write(head, 0, head.Length);
        stream.Write(body, 0, body.Length);
        stream.Flush();
      }
      catch
      {
      }
      finally
      {
        try { client.Close(); } catch { }
      }
    }

    static string PlayerPage()
    {
      return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"referrer\" content=\"strict-origin-when-cross-origin\"><style>html,body,#box{margin:0;width:100%;height:100%;background:#000;overflow:hidden}#box{position:fixed;left:0;top:0}iframe{width:100%;height:100%;border:0;display:block}</style></head><body><div id=\"box\"></div><script src=\"https://www.youtube.com/iframe_api\"></script><script>var last='';var api=false;var player=null;var closing=false;var vol=100;function onYouTubeIframeAPIReady(){api=true;}function applyVol(n){vol=n;if(player&&player.setVolume){try{player.setVolume(n);if(n>0)player.unMute();}catch(e){}}}function clearBox(){if(player&&player.destroy){try{player.destroy();}catch(e){}}player=null;var box=document.getElementById('box');if(box)box.innerHTML='';}function ended(){if(closing)return;closing=true;fetch('/player/done',{cache:'no-store'}).catch(function(){});}function show(j){clearBox();closing=false;if(typeof j.volume==='number')vol=j.volume;var id='';var m=(j.embed||'').match(/embed\\/([^?&#]+)/);if(m)id=m[1];if(j.host==='YouTube'&&id&&api&&window.YT&&YT.Player){document.getElementById('box').innerHTML='<div id=\"yt\"></div>';player=new YT.Player('yt',{width:'100%',height:'100%',videoId:id,playerVars:{autoplay:1,rel:0,modestbranding:1,playsinline:1,controls:1},events:{onReady:function(e){try{e.target.unMute();e.target.setVolume(vol);e.target.playVideo();}catch(err){}},onError:function(){ended();},onStateChange:function(e){if(e.data!==0)return;var d=0,t=0;try{d=player.getDuration();t=player.getCurrentTime();}catch(err){}if(d>1&&t+1.5<d)return;ended();}}});return;}var f=document.createElement('iframe');f.setAttribute('referrerpolicy','strict-origin-when-cross-origin');f.setAttribute('allow','autoplay; encrypted-media; picture-in-picture; fullscreen');f.setAttribute('allowfullscreen','');f.src=j.embed;document.getElementById('box').appendChild(f);}async function tick(){try{var r=await fetch('/player/now',{cache:'no-store'});var j=await r.json();if(typeof j.volume==='number'&&j.volume!==vol)applyVol(j.volume);if(!j.embed){if(last){clearBox();last='';}return;}if(j.id!==last){last=j.id;show(j);}}catch(e){}}setInterval(tick,500);tick();</script></body></html>";
    }

    static string FeedJsonText(OverlayStyle style, List<OverlayLine> lines)
    {
      long now = NowMs();
      int hold = style.Hold < 2 ? 8 : style.Hold;
      long holdMs = hold * 1000L;
      int max = style.Lines < 1 ? 8 : style.Lines;
      List<OverlayLine> live = new List<OverlayLine>();
      int i;
      for (i = 0; i < lines.Count; i++)
      {
        OverlayLine line = lines[i];
        long age = line.Born <= 0 ? 0 : now - line.Born;
        if (age <= holdMs) live.Add(line);
      }
      if (live.Count > max) live.RemoveRange(0, live.Count - max);
      StringBuilder sb = new StringBuilder();
      sb.Append("{\"style\":{");
      sb.Append("\"font\":").Append(JsonConvert.ToString(style.Font == null || style.Font.Length == 0 ? "Segoe UI" : style.Font));
      sb.Append(",\"size\":").Append(style.Size.ToString(CultureInfo.InvariantCulture));
      sb.Append(",\"bold\":").Append(style.Bold ? "true" : "false");
      sb.Append(",\"color\":").Append(JsonConvert.ToString(style.Color == null || style.Color.Length == 0 ? "#F4EFE6" : style.Color));
      sb.Append(",\"nick\":").Append(JsonConvert.ToString(style.Nick == null || style.Nick.Length == 0 ? "#FF8A3D" : style.Nick));
      sb.Append(",\"stroke\":").Append(JsonConvert.ToString(style.Stroke == null || style.Stroke.Length == 0 ? "shadow" : style.Stroke));
      sb.Append(",\"plate\":").Append(style.Plate ? "true" : "false");
      sb.Append(",\"align\":").Append(JsonConvert.ToString(style.Align == "top" ? "top" : "bottom"));
      sb.Append("},\"messages\":[");
      for (i = 0; i < live.Count; i++)
      {
        if (i > 0) sb.Append(',');
        OverlayLine line = live[i];
        long age = line.Born <= 0 ? 0 : now - line.Born;
        long left = holdMs - age;
        if (left < 0) left = 0;
        sb.Append("{\"author\":").Append(JsonConvert.ToString(line.Author ?? ""));
        sb.Append(",\"text\":").Append(JsonConvert.ToString(line.Text ?? ""));
        sb.Append(",\"mark\":").Append(JsonConvert.ToString(style.Platform ? (line.Mark ?? "") : ""));
        sb.Append(",\"time\":").Append(JsonConvert.ToString(style.Clock ? (line.Time ?? "") : ""));
        sb.Append(",\"icons\":[");
        if (line.Icons != null && line.Icons.Length > 0)
        {
          string[] icons = line.Icons.Split('\n');
          bool anyIcon = false;
          int k;
          for (k = 0; k < icons.Length; k++)
          {
            if (icons[k].Length == 0) continue;
            if (anyIcon) sb.Append(',');
            anyIcon = true;
            sb.Append(JsonConvert.ToString(icons[k]));
          }
        }
        sb.Append(']');
        sb.Append(",\"left\":").Append(left.ToString(CultureInfo.InvariantCulture));
        sb.Append('}');
      }
      sb.Append("]}");
      return sb.ToString();
    }

    static string Page()
    {
      return "<!doctype html><html><head><meta charset='utf-8'><title>RECH</title><style>"
        + "html,body{margin:0;width:100%;height:100%;background:rgba(0,0,0,0)!important;overflow:hidden;}"
        + "#log{position:absolute;left:0;right:0;display:flex;flex-direction:column;gap:8px;padding:8px 12px;}"
        + "#log.bottom{bottom:0;}#log.top{top:0;}"
        + "#log .row{line-height:1.2;max-width:100%;}"
        + "#log.plate .row{background:rgba(14,12,10,.78);border-radius:12px;padding:6px 12px 8px;width:fit-content;max-width:100%;}"
        + "#log .nick{color:var(--nick);}#log .mark,#log .time{opacity:.85;font-size:.62em;margin-right:8px;}"
        + "#log .emo{height:1.15em;width:auto;vertical-align:-0.25em;margin:0 2px;}"
        + "#log.shadow .row{text-shadow:0 2px 0 #000,0 0 10px #000;}"
        + "#log.stroke .nick,#log.stroke .text{-webkit-text-stroke:.045em #000;paint-order:stroke fill;}"
        + "</style></head><body><div id='log'></div><script>"
        + "function esc(s){return String(s==null?'':s).replace(/[&<>]/g,function(c){return c=='&'?'&'+'amp;':c=='<'?'&'+'lt;':'&'+'gt;';});}"
        + "function body(m){var parts=String(m.text||'').split('\\uFFFC');var icons=m.icons||[];var html='';var k;"
        + "for(k=0;k<parts.length;k++){html+=esc(parts[k]);if(k<parts.length-1){var u=icons[k]||'';if(/^https?:\\/\\//.test(u))html+='<img class=\"emo\" src=\"'+esc(u)+'\" alt=\"\">';}}"
        + "return html;}"
        + "function apply(data){if(!data)return;var s=data.style||{};var root=document.getElementById('log');"
        + "var stroke=s.stroke||'shadow';"
        + "root.className=(s.align=='top'?'top':'bottom')+(s.plate?' plate':'')+' '+(stroke=='none'?'plain':stroke);"
        + "var font=String(s.font||'Segoe UI').replace(/[\"<>]/g,'');"
        + "root.style.fontFamily='\"'+font+'\",Segoe UI,sans-serif';"
        + "root.style.fontSize=(s.size||28)+'px';"
        + "root.style.fontWeight=s.bold===false?'500':'700';"
        + "root.style.color=s.color||'#F4EFE6';"
        + "root.style.setProperty('--nick',s.nick||'#FF8A3D');"
        + "var lines=data.messages||[];var html='';var i;"
        + "for(i=0;i<lines.length;i++){var m=lines[i];var op=1;if(typeof m.left=='number'&&m.left<700)op=Math.max(0,m.left/700);"
        + "var meta='';if(m.mark)meta+='<span class=\"mark\">'+esc(m.mark)+'</span>';"
        + "if(m.time)meta+='<span class=\"time\">'+esc(m.time)+'</span>';"
        + "html+='<div class=\"row\" style=\"opacity:'+op+'\"><div class=\"head\">'+meta+'<span class=\"nick\">'+esc(m.author)+'</span></div><div class=\"text\">'+body(m)+'</div></div>';}"
        + "root.innerHTML=html;}"
        + "function tick(){var x=new XMLHttpRequest();x.open('GET','/feed?t='+Date.now(),true);"
        + "x.onload=function(){if(x.status!=200)return;try{apply(JSON.parse(x.responseText));}catch(e){}};"
        + "x.send();}"
        + "setInterval(tick,250);tick();"
        + "</script></body></html>";
    }
  }

  public class CrowdBit
  {
    public string Platform;
    public string Title;
    public int Count;
    public bool Live;
  }

  public class ReaderWindow : Form
  {
    readonly OverlayHub hub;
    readonly RichTextBox box;
    readonly ComboBox fontBox;
    readonly TrackBar sizeBar;
    readonly Button textButton;
    readonly Button nickButton;
    readonly Button backButton;
    readonly Panel crowd;
    readonly Panel lookBar;
    int placedN = -1;
    int placedW = -1;
    string crowdSig = "";
    readonly Button downButton;
    readonly LinkLabel siteLink;
    readonly Label sizeLabel;
    readonly System.Windows.Forms.Timer timer;
    int seen = -1;
    int drawn;
    int artSeen;
    string tail = "";
    bool filling;
    public string FontName = "Segoe UI";
    public int FontSize = 18;
    public Color TextColor = Color.FromArgb(246, 239, 228);
    public Color NickColor = Color.FromArgb(255, 122, 60);
    public Color Paper = Color.FromArgb(17, 14, 12);
    public event Action<Rectangle> Placed;
    public event Action LookChanged;
    public Func<List<CrowdBit>> Crowd;
    Font barFont;

    public ReaderWindow(OverlayHub hub)
    {
      this.hub = hub;
      Text = "ЛюсиЧат — мои сообщения";
      FormBorderStyle = FormBorderStyle.Sizable;
      MinimumSize = new Size(980, 280);
      BackColor = Paper;
      ShowInTaskbar = true;
      box = new RichTextBox();
      box.Dock = DockStyle.Fill;
      box.ReadOnly = true;
      box.BorderStyle = BorderStyle.None;
      box.BackColor = Paper;
      box.ForeColor = TextColor;
      box.DetectUrls = false;
      box.HideSelection = true;
      box.ScrollBars = RichTextBoxScrollBars.Vertical;
      box.Font = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Pixel);
      barFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
      lookBar = new Panel();
      lookBar.Dock = DockStyle.Bottom;
      lookBar.Height = 40;
      lookBar.BackColor = Color.FromArgb(22, 17, 14);
      fontBox = new ComboBox();
      fontBox.DropDownStyle = ComboBoxStyle.DropDownList;
      fontBox.Font = barFont;
      fontBox.BackColor = Color.FromArgb(36, 25, 16);
      fontBox.ForeColor = Color.FromArgb(246, 239, 228);
      fontBox.Items.AddRange(new object[] { "Segoe UI", "Arial", "Verdana", "Tahoma", "Georgia", "Consolas", "Times New Roman" });
      sizeLabel = new Label();
      sizeLabel.Font = barFont;
      sizeLabel.ForeColor = Color.FromArgb(231, 196, 138);
      sizeLabel.Text = "18";
      sizeLabel.TextAlign = ContentAlignment.MiddleRight;
      sizeBar = new TrackBar();
      sizeBar.Minimum = 12;
      sizeBar.Maximum = 36;
      sizeBar.TickFrequency = 4;
      sizeBar.BackColor = lookBar.BackColor;
      textButton = Chip("Текст");
      nickButton = Chip("Ник");
      backButton = Chip("Фон");
      downButton = Chip("Вниз");
      downButton.Click += delegate
      {
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
      };
      crowd = new QuietPanel();
      crowd.BackColor = lookBar.BackColor;
      crowd.Paint += new PaintEventHandler(PaintCrowd);
      LinkLabel author = new LinkLabel();
      siteLink = author;
      author.Text = "ghostpot.ru";
      author.Font = barFont;
      author.LinkColor = Color.FromArgb(231, 196, 138);
      author.ActiveLinkColor = Color.FromArgb(255, 122, 60);
      author.VisitedLinkColor = Color.FromArgb(231, 196, 138);
      author.LinkBehavior = LinkBehavior.HoverUnderline;
      author.AutoSize = true;
      author.Cursor = Cursors.Hand;
      author.LinkClicked += delegate
      {
        try
        {
          Process.Start(new ProcessStartInfo("https://ghostpot.ru") { UseShellExecute = true });
        }
        catch
        {
        }
      };
      lookBar.Resize += delegate { PlaceBar(author); };
      lookBar.Controls.Add(fontBox);
      lookBar.Controls.Add(sizeLabel);
      lookBar.Controls.Add(sizeBar);
      lookBar.Controls.Add(textButton);
      lookBar.Controls.Add(nickButton);
      lookBar.Controls.Add(backButton);
      lookBar.Controls.Add(downButton);
      lookBar.Controls.Add(author);
      lookBar.Controls.Add(crowd);
      Controls.Add(box);
      Controls.Add(lookBar);
      fontBox.SelectedIndexChanged += delegate { if (!filling) CommitLook(); };
      sizeBar.Scroll += delegate
      {
        sizeLabel.Text = sizeBar.Value.ToString(CultureInfo.InvariantCulture);
        if (!filling) CommitLook();
      };
      textButton.Click += delegate { Pick(true); };
      nickButton.Click += delegate { Pick(false); };
      backButton.Click += delegate { PickPaper(); };
      timer = new System.Windows.Forms.Timer();
      timer.Interval = 200;
      timer.Tick += delegate { Pump(); };
    }

    class QuietPanel : Panel
    {
      public QuietPanel()
      {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        UpdateStyles();
      }
    }

    static Button Chip(string text)
    {
      Button button = new Button();
      button.Text = text;
      button.FlatStyle = FlatStyle.Flat;
      button.ForeColor = Color.FromArgb(246, 239, 228);
      button.BackColor = Color.FromArgb(36, 25, 16);
      button.FlatAppearance.BorderColor = Color.FromArgb(58, 48, 40);
      button.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
      return button;
    }

    public void ApplyLook(string font, int size, Color text, Color nick, Color paper)
    {
      filling = true;
      FontName = font == null || font.Length == 0 ? "Segoe UI" : font;
      FontSize = size < 12 || size > 36 ? 18 : size;
      TextColor = text;
      NickColor = nick;
      Paper = paper;
      int index = fontBox.Items.IndexOf(FontName);
      if (index < 0)
      {
        fontBox.Items.Add(FontName);
        index = fontBox.Items.Count - 1;
      }
      fontBox.SelectedIndex = index;
      sizeBar.Value = FontSize;
      sizeLabel.Text = FontSize.ToString(CultureInfo.InvariantCulture);
      textButton.BackColor = TextColor;
      nickButton.BackColor = NickColor;
      backButton.BackColor = Paper;
      filling = false;
      Restyle();
    }

    void CommitLook()
    {
      FontName = fontBox.SelectedIndex >= 0 ? Convert.ToString(fontBox.SelectedItem) : "Segoe UI";
      FontSize = sizeBar.Value;
      Restyle();
      if (LookChanged != null) LookChanged();
    }

    void Pick(bool text)
    {
      ColorDialog dialog = new ColorDialog();
      dialog.FullOpen = true;
      dialog.Color = text ? TextColor : NickColor;
      if (dialog.ShowDialog(this) != DialogResult.OK) return;
      if (text)
      {
        TextColor = dialog.Color;
        textButton.BackColor = dialog.Color;
      }
      else
      {
        NickColor = dialog.Color;
        nickButton.BackColor = dialog.Color;
      }
      Restyle();
      if (LookChanged != null) LookChanged();
    }

    void PickPaper()
    {
      ColorDialog dialog = new ColorDialog();
      dialog.FullOpen = true;
      dialog.Color = Paper;
      if (dialog.ShowDialog(this) != DialogResult.OK) return;
      Paper = dialog.Color;
      backButton.BackColor = Paper;
      Restyle();
      if (LookChanged != null) LookChanged();
    }

    void Restyle()
    {
      drawn = 0;
      tail = "";
      seen = -1;
      box.ReadOnly = false;
      box.Clear();
      box.ReadOnly = true;
      try
      {
        box.Font = new Font(FontName, FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
      }
      catch
      {
        box.Font = new Font("Segoe UI", FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
      }
      box.BackColor = Paper;
      BackColor = Paper;
      Pump();
    }

    protected override void OnShown(EventArgs e)
    {
      base.OnShown(e);
      timer.Start();
      Pump();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
      timer.Stop();
      if (Placed != null) Placed(Bounds);
      base.OnFormClosed(e);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
      base.OnResizeEnd(e);
      if (Placed != null) Placed(Bounds);
    }

    static string Key(OverlayLine line)
    {
      if (line == null) return "";
      return (line.Mark ?? "") + "|" + (line.Time ?? "") + "|" + (line.Author ?? "") + "|" + (line.Text ?? "");
    }

    bool AtBottom()
    {
      if (box.TextLength <= 1) return true;
      int vis = box.GetCharIndexFromPosition(new Point(4, box.ClientSize.Height - 4));
      return vis >= box.TextLength - 2;
    }

    void Pump()
    {
      PlaceBar(siteLink);
      string sig = CrowdSig();
      if (sig != crowdSig)
      {
        crowdSig = sig;
        if (crowd != null) crowd.Invalidate();
      }
      List<OverlayLine> lines = new List<OverlayLine>();
      OverlayStyle ignored;
      hub.Snapshot(lines, out ignored);
      int revision = hub.Revision;
      int art = EmoteArt.Stamp;
      string last = lines.Count == 0 ? "" : Key(lines[lines.Count - 1]);
      if (revision == seen && last == tail && drawn == lines.Count && art == artSeen) return;
      bool artMoved = art != artSeen;
      seen = revision;
      artSeen = art;
      bool bottom = AtBottom();
      bool extend = !artMoved && drawn > 0 && drawn <= lines.Count && tail == Key(lines[drawn - 1]);
      int start = extend ? drawn : 0;
      if (!extend)
      {
        box.ReadOnly = false;
        box.Clear();
        box.ReadOnly = true;
      }
      int i;
      for (i = start; i < lines.Count; i++) Append(lines[i]);
      drawn = lines.Count;
      tail = last;
      if (bottom || !extend)
      {
        box.SelectionStart = box.TextLength;
        box.ScrollToCaret();
      }
    }

    string CrowdSig()
    {
      try
      {
        List<CrowdBit> bits = Crowd == null ? null : Crowd();
        if (bits == null || bits.Count == 0) return "";
        StringBuilder text = new StringBuilder();
        int i;
        for (i = 0; i < bits.Count; i++)
        {
          CrowdBit bit = bits[i];
          text.Append(bit.Platform);
          text.Append(bit.Live ? '1' : '0');
          text.Append(bit.Count);
          text.Append(';');
        }
        return text.ToString();
      }
      catch
      {
        return "";
      }
    }

    void PlaceBar(LinkLabel author)
    {
      if (lookBar == null) return;
      int n = 1;
      try
      {
        List<CrowdBit> bits = Crowd == null ? null : Crowd();
        if (bits != null && bits.Count > 0) n = bits.Count;
      }
      catch
      {
      }
      int crowdW = 8 + n * 56;
      int limit = lookBar.Width - 560;
      if (limit < 64) limit = 64;
      if (crowdW > limit) crowdW = limit;
      if (placedN == n && placedW == lookBar.Width && crowd.Width == crowdW) return;
      placedN = n;
      placedW = lookBar.Width;
      crowd.SetBounds(6, 4, crowdW, 32);
      int x = crowd.Right + 8;
      fontBox.SetBounds(x, 6, 116, 26);
      sizeLabel.SetBounds(x + 118, 8, 28, 22);
      sizeBar.SetBounds(x + 146, 2, 88, 34);
      textButton.SetBounds(x + 238, 6, 58, 26);
      nickButton.SetBounds(x + 300, 6, 48, 26);
      backButton.SetBounds(x + 352, 6, 48, 26);
      downButton.SetBounds(x + 404, 6, 56, 26);
      if (author != null)
      {
        author.Location = new Point(Math.Max(downButton.Right + 8, lookBar.Width - author.Width - 8), 10);
        author.Visible = author.Left > downButton.Right;
      }
    }

    void PaintCrowd(object sender, PaintEventArgs e)
    {
      List<CrowdBit> bits = Crowd == null ? null : Crowd();
      Graphics g = e.Graphics;
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
      Font font = barFont == null ? Font : barFont;
      if (bits == null || bits.Count == 0)
      {
        TextRenderer.DrawText(g, "нет каналов", font, new Rectangle(0, 4, crowd.Width, 24), Color.FromArgb(183, 170, 154), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        return;
      }
      int x = 0;
      int total = 0;
      int known = 0;
      int i;
      for (i = 0; i < bits.Count; i++)
      {
        CrowdBit bit = bits[i];
        if (x > crowd.Width - 52) break;
        Rectangle badge = new Rectangle(x, 5, 20, 20);
        using (SolidBrush brush = new SolidBrush(MainForm.BadgeInk(bit.Platform))) g.FillEllipse(brush, badge);
        string mark = MainForm.PlatformMark(bit.Platform);
        if (mark.Length > 2) mark = mark.Substring(0, 2);
        TextRenderer.DrawText(g, mark, font, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        string count = bit.Count < 0 ? "—" : bit.Count.ToString(CultureInfo.InvariantCulture);
        Color ink = bit.Live ? Color.FromArgb(120, 210, 140) : Color.FromArgb(183, 170, 154);
        TextRenderer.DrawText(g, count, font, new Rectangle(x + 22, 4, 30, 22), ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (bit.Live && bit.Count > 0)
        {
          total += bit.Count;
          known++;
        }
        x += 56;
      }
      if (known > 0 && x + 54 < crowd.Width) TextRenderer.DrawText(g, total.ToString(CultureInfo.InvariantCulture), font, new Rectangle(x, 4, 48, 22), Color.FromArgb(231, 196, 138), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    void Append(OverlayLine line)
    {
      box.ReadOnly = false;
      int nickAt = box.TextLength;
      string head = "";
      if (line.Mark != null && line.Mark.Length > 0) head = line.Mark + "  ";
      if (line.Time != null && line.Time.Length > 0) head = head + line.Time + "  ";
      head = head + (line.Author ?? "") + "\n";
      box.AppendText(head);
      box.Select(nickAt, head.Length);
      box.SelectionFont = box.Font;
      box.SelectionColor = NickColor;
      int bodyAt = box.TextLength;
      string body = line.Text ?? "";
      string[] icons = line.Icons == null || line.Icons.Length == 0 ? new string[0] : line.Icons.Split('\n');
      int mark = 0;
      int at = 0;
      while (at <= body.Length)
      {
        int hit = body.IndexOf('\uFFFC', at);
        if (hit < 0) hit = body.Length;
        if (hit > at)
        {
          string chunk = body.Substring(at, hit - at);
          int start = box.TextLength;
          box.AppendText(chunk);
          box.Select(start, chunk.Length);
          box.SelectionFont = box.Font;
          box.SelectionColor = TextColor;
        }
        if (hit >= body.Length) break;
        string url = mark < icons.Length ? icons[mark] : "";
        mark = mark + 1;
        Image image = EmoteArt.Peek(url);
        if (image != null)
        {
          string pict = EmoteArt.Pict(image);
          if (pict.Length > 0)
          {
            box.Select(box.TextLength, 0);
            box.SelectedRtf = "{\\rtf1\\ansi " + pict + "}";
          }
        }
        at = hit + 1;
      }
      int tailAt = box.TextLength;
      box.AppendText("\n\n");
      box.Select(tailAt, 2);
      box.SelectionFont = box.Font;
      box.SelectionColor = TextColor;
      box.Select(box.TextLength, 0);
      box.ReadOnly = true;
    }
  }

  public class ChatWindow : Form
  {
    readonly OverlayHub hub;
    readonly System.Windows.Forms.Timer timer;
    int seenRevision = -1;
    bool dragging;
    int resizeMode;
    bool hovered;
    Point startScreen;
    Rectangle startBounds;
    public event Action<Rectangle> Placed;

    public ChatWindow(OverlayHub hub)
    {
      this.hub = hub;
      Text = "РЕЧЬ — чат";
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = true;
      TopMost = true;
      BackColor = Color.Black;
      MinimumSize = new Size(160, 90);
      timer = new System.Windows.Forms.Timer();
      timer.Interval = 120;
      timer.Tick += delegate { Pump(); };
      ContextMenuStrip menu = new ContextMenuStrip();
      ToolStripMenuItem closeItem = new ToolStripMenuItem("Закрыть окно");
      closeItem.Click += delegate { Close(); };
      menu.Items.Add(closeItem);
      ContextMenuStrip = menu;
    }

    protected override CreateParams CreateParams
    {
      get
      {
        CreateParams cp = base.CreateParams;
        cp.ExStyle = cp.ExStyle | 0x00080000;
        return cp;
      }
    }

    protected override void OnShown(EventArgs e)
    {
      base.OnShown(e);
      timer.Start();
      Present();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
      timer.Stop();
      if (Placed != null) Placed(Bounds);
      base.OnFormClosed(e);
    }

    protected override void OnResize(EventArgs e)
    {
      base.OnResize(e);
      if (IsHandleCreated && WindowState == FormWindowState.Normal) Present();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
      hovered = true;
      Present();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
      hovered = false;
      if (resizeMode == 0 && !dragging) Present();
    }

    int Hit(int x, int y)
    {
      int m = 16;
      bool left = x < m;
      bool right = x >= Width - m;
      bool top = y < m;
      bool bottom = y >= Height - m;
      if (right && bottom) return 8;
      if (left && bottom) return 7;
      if (right && top) return 6;
      if (left && top) return 5;
      if (right) return 4;
      if (left) return 3;
      if (bottom) return 2;
      if (top) return 1;
      return 0;
    }

    Cursor HitCursor(int hit)
    {
      if (hit == 5 || hit == 8) return Cursors.SizeNWSE;
      if (hit == 6 || hit == 7) return Cursors.SizeNESW;
      if (hit == 3 || hit == 4) return Cursors.SizeWE;
      if (hit == 1 || hit == 2) return Cursors.SizeNS;
      return Cursors.SizeAll;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;
      resizeMode = Hit(e.X, e.Y);
      dragging = resizeMode == 0;
      startScreen = PointToScreen(e.Location);
      startBounds = Bounds;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
      if (resizeMode == 0 && !dragging) Cursor = HitCursor(Hit(e.X, e.Y));
      if (resizeMode == 0 && !dragging) return;
      Point now = PointToScreen(e.Location);
      int dx = now.X - startScreen.X;
      int dy = now.Y - startScreen.Y;
      if (dragging)
      {
        Location = new Point(startBounds.X + dx, startBounds.Y + dy);
        Present();
        return;
      }
      Rectangle next = startBounds;
      if (resizeMode == 4 || resizeMode == 6 || resizeMode == 8) next.Width = startBounds.Width + dx;
      if (resizeMode == 3 || resizeMode == 5 || resizeMode == 7)
      {
        next.X = startBounds.X + dx;
        next.Width = startBounds.Width - dx;
      }
      if (resizeMode == 2 || resizeMode == 7 || resizeMode == 8) next.Height = startBounds.Height + dy;
      if (resizeMode == 1 || resizeMode == 5 || resizeMode == 6)
      {
        next.Y = startBounds.Y + dy;
        next.Height = startBounds.Height - dy;
      }
      if (next.Width < 160) next.Width = 160;
      if (next.Height < 90) next.Height = 90;
      if (next.Width > 1920) next.Width = 1920;
      if (next.Height > 1080) next.Height = 1080;
      Bounds = next;
      Present();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
      bool moved = dragging || resizeMode != 0;
      dragging = false;
      resizeMode = 0;
      Present();
      if (moved && Placed != null) Placed(Bounds);
    }

    void Pump()
    {
      int revision = hub.Revision;
      bool live = false;
      List<OverlayLine> probe = new List<OverlayLine>();
      OverlayStyle style;
      hub.Snapshot(probe, out style);
      long now = OverlayHub.NowMs();
      int hold = style == null || style.Hold < 2 ? 8 : style.Hold;
      int i;
      for (i = 0; i < probe.Count; i++)
      {
        if (now - probe[i].Born < (hold * 1000L) + 400) live = true;
      }
      if (revision == seenRevision && !live && resizeMode == 0 && !dragging) return;
      seenRevision = revision;
      Present();
    }

    void Present()
    {
      if (!IsHandleCreated || Width < 20 || Height < 20) return;
      List<OverlayLine> lines = new List<OverlayLine>();
      OverlayStyle style;
      hub.Snapshot(lines, out style);
      Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
      using (Graphics g = Graphics.FromImage(bitmap))
      {
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        PaintChat(g, lines, style);
      }
      Layered.Blit(this, bitmap);
      bitmap.Dispose();
    }

    void PaintChat(Graphics g, List<OverlayLine> lines, OverlayStyle style)
    {
      if (style == null) style = new OverlayStyle();
      float size = style.Size < 18 ? 32 : style.Size;
      Font font = MakeFont(style.Font, size, style.Bold);
      Font nickFont = MakeFont(style.Font, size, true);
      Color ink = ParseInk(style.Color, Color.FromArgb(244, 239, 230));
      Color nick = ParseInk(style.Nick, Color.FromArgb(255, 138, 61));
      float width = Width - 36;
      int max = style.Lines < 1 ? 8 : style.Lines;
      long now = OverlayHub.NowMs();
      int holdMs = (style.Hold < 2 ? 8 : style.Hold) * 1000;
      List<int> visible = new List<int>();
      int i;
      for (i = lines.Count - 1; i >= 0 && visible.Count < max; i--)
      {
        long age = now - lines[i].Born;
        if (lines[i].Born > 0 && age > holdMs) continue;
        visible.Insert(0, i);
      }
      float y = style.Align == "top" ? 16 : Height - 16;
      List<RectangleF> blocks = new List<RectangleF>();
      List<string> heads = new List<string>();
      List<string> bodies = new List<string>();
      List<float> fades = new List<float>();
      for (i = 0; i < visible.Count; i++)
      {
        OverlayLine line = lines[visible[i]];
        long age = now - line.Born;
        float fade = 1f;
        if (line.Born > 0 && age > holdMs - 700)
        {
          fade = (holdMs - age) / 700f;
          if (fade < 0f) fade = 0f;
        }
        string head = "";
        if (style.Platform && line.Mark != null && line.Mark.Length > 0) head = line.Mark + "  ";
        if (style.Clock && line.Time != null && line.Time.Length > 0) head = head + line.Time + "  ";
        head = head + (line.Author == null ? "" : line.Author);
        string body = line.Text == null ? "" : line.Text;
        SizeF headSize = g.MeasureString(head, nickFont, (int)width);
        SizeF bodySize = g.MeasureString(body, font, (int)width);
        float block = headSize.Height + bodySize.Height + (style.Plate ? 16 : 8);
        float top;
        if (style.Align == "top")
        {
          top = y;
          y = y + block + 8;
        }
        else
        {
          y = y - block - 8;
          top = y + 8;
        }
        blocks.Add(new RectangleF(16, top, width, block));
        heads.Add(head);
        bodies.Add(body);
        fades.Add(fade);
      }
      for (i = 0; i < blocks.Count; i++)
      {
        RectangleF rect = blocks[i];
        float fade = fades[i];
        if (style.Plate)
        {
          RectangleF plate = rect;
          plate.Inflate(4, 0);
          using (GraphicsPath path = Round(plate, 14))
          using (SolidBrush plateBrush = new SolidBrush(Color.FromArgb((int)(168 * fade), 14, 12, 10)))
          {
            g.FillPath(plateBrush, path);
          }
        }
        float textTop = rect.Y + (style.Plate ? 8 : 0);
        DrawLine(g, heads[i], nickFont, Scale(nick, fade), new RectangleF(rect.X, textTop, width, rect.Height), style.Stroke, fade);
        SizeF used = g.MeasureString(heads[i], nickFont, (int)width);
        DrawLine(g, bodies[i], font, Scale(ink, fade), new RectangleF(rect.X, textTop + used.Height, width, rect.Height), style.Stroke, fade);
      }
      if (visible.Count == 0)
      {
        using (Font hint = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
        {
          g.DrawString("РЕЧЬ", hint, brush, 16, 12);
        }
      }
      if (hovered || resizeMode != 0 || dragging)
      {
        using (Pen pen = new Pen(Color.FromArgb(230, 226, 74, 27), 2f))
        {
          g.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
        }
        using (SolidBrush handle = new SolidBrush(Color.FromArgb(230, 226, 74, 27)))
        {
          g.FillRectangle(handle, 0, 0, 8, 8);
          g.FillRectangle(handle, Width - 8, 0, 8, 8);
          g.FillRectangle(handle, 0, Height - 8, 8, 8);
          g.FillRectangle(handle, Width - 8, Height - 8, 8, 8);
        }
        string label = Width.ToString(CultureInfo.InvariantCulture) + " × " + Height.ToString(CultureInfo.InvariantCulture);
        using (Font hint = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (SolidBrush plate = new SolidBrush(Color.FromArgb(210, 20, 16, 12)))
        using (SolidBrush white = new SolidBrush(Color.White))
        {
          SizeF ls = g.MeasureString(label, hint);
          g.FillRectangle(plate, 12, 10, ls.Width + 8, ls.Height + 2);
          g.DrawString(label, hint, white, 16, 11);
        }
      }
      font.Dispose();
      nickFont.Dispose();
    }

    static Color Scale(Color color, float fade)
    {
      int alpha = (int)Math.Round(color.A * fade);
      if (alpha < 0) alpha = 0;
      if (alpha > 255) alpha = 255;
      return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    static void DrawLine(Graphics g, string text, Font font, Color color, RectangleF rect, string stroke, float fade)
    {
      if (text == null || text.Length == 0 || fade <= 0.01f) return;
      if (stroke == "stroke")
      {
        using (GraphicsPath path = new GraphicsPath())
        {
          float em = font.Size;
          path.AddString(text, font.FontFamily, (int)font.Style, em, rect, new StringFormat());
          using (Pen pen = new Pen(Color.FromArgb((int)(230 * fade), 0, 0, 0), Math.Max(2f, font.Size / 9f)))
          {
            pen.LineJoin = LineJoin.Round;
            g.DrawPath(pen, path);
          }
          using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
        }
        return;
      }
      if (stroke != "none")
      {
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)(190 * fade), 0, 0, 0)))
        {
          RectangleF shifted = rect;
          shifted.Offset(0, Math.Max(1f, font.Size / 14f));
          g.DrawString(text, font, shadow, shifted);
        }
      }
      using (SolidBrush brush = new SolidBrush(color)) g.DrawString(text, font, brush, rect);
    }

    static Font MakeFont(string family, float size, bool bold)
    {
      FontStyle fs = bold ? FontStyle.Bold : FontStyle.Regular;
      try
      {
        return new Font(family == null || family.Length == 0 ? "Segoe UI" : family, size, fs, GraphicsUnit.Pixel);
      }
      catch
      {
        return new Font("Segoe UI", size, fs, GraphicsUnit.Pixel);
      }
    }

    static Color ParseInk(string text, Color fallback)
    {
      if (text == null || text.Length == 0) return fallback;
      try { return ColorTranslator.FromHtml(text); }
      catch { return fallback; }
    }

    static GraphicsPath Round(RectangleF rect, float radius)
    {
      GraphicsPath path = new GraphicsPath();
      float d = radius * 2f;
      if (rect.Width < d || rect.Height < d)
      {
        path.AddRectangle(rect);
        return path;
      }
      path.AddArc(rect.X, rect.Y, d, d, 180, 90);
      path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
      path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
      path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
      path.CloseFigure();
      return path;
    }
  }

  static class Layered
  {
    public static void Blit(Form form, Bitmap bitmap)
    {
      IntPtr screen = GetDC(IntPtr.Zero);
      IntPtr mem = CreateCompatibleDC(screen);
      IntPtr bits;
      IntPtr dib = Dib(bitmap, out bits);
      IntPtr old = SelectObject(mem, dib);
      WinPoint dst = new WinPoint();
      dst.X = form.Left;
      dst.Y = form.Top;
      WinSize size = new WinSize();
      size.CX = bitmap.Width;
      size.CY = bitmap.Height;
      WinPoint src = new WinPoint();
      Blend blend = new Blend();
      blend.Op = 0;
      blend.Flags = 0;
      blend.Alpha = 255;
      blend.Format = 1;
      UpdateLayeredWindow(form.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);
      SelectObject(mem, old);
      DeleteObject(dib);
      DeleteDC(mem);
      ReleaseDC(IntPtr.Zero, screen);
    }

    static IntPtr Dib(Bitmap bitmap, out IntPtr bits)
    {
      int width = bitmap.Width;
      int height = bitmap.Height;
      BitInfo info = new BitInfo();
      info.biSize = 40;
      info.biWidth = width;
      info.biHeight = -height;
      info.biPlanes = 1;
      info.biBitCount = 32;
      IntPtr screen = GetDC(IntPtr.Zero);
      IntPtr dib = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
      ReleaseDC(IntPtr.Zero, screen);
      BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
      int srcStride = data.Stride;
      byte[] raw = new byte[srcStride * height];
      System.Runtime.InteropServices.Marshal.Copy(data.Scan0, raw, 0, raw.Length);
      bitmap.UnlockBits(data);
      byte[] premul = new byte[width * height * 4];
      int y;
      for (y = 0; y < height; y++)
      {
        int row = y * srcStride;
        int dest = y * width * 4;
        int x;
        for (x = 0; x < width; x++)
        {
          int i = row + x * 4;
          int o = dest + x * 4;
          byte b = raw[i];
          byte g = raw[i + 1];
          byte r = raw[i + 2];
          byte a = raw[i + 3];
          premul[o] = (byte)(b * a / 255);
          premul[o + 1] = (byte)(g * a / 255);
          premul[o + 2] = (byte)(r * a / 255);
          premul[o + 3] = a;
        }
      }
      System.Runtime.InteropServices.Marshal.Copy(premul, 0, bits, premul.Length);
      return dib;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinPoint
    {
      public int X;
      public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WinSize
    {
      public int CX;
      public int CY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Blend
    {
      public byte Op;
      public byte Flags;
      public byte Alpha;
      public byte Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitInfo
    {
      public int biSize;
      public int biWidth;
      public int biHeight;
      public short biPlanes;
      public short biBitCount;
      public int biCompression;
      public int biSizeImage;
      public int biXPelsPerMeter;
      public int biYPelsPerMeter;
      public int biClrUsed;
      public int biClrImportant;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref WinPoint pptDst, ref WinSize psize, IntPtr hdcSrc, ref WinPoint pptSrc, int crKey, ref Blend pblend, int dwFlags);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitInfo pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
  }

  public class GameLine
  {
    public string When;
    public string Original;
    public string Spoken;
    public string Path;
    public string Shot;
    public override string ToString()
    {
      string left = Original == null ? "" : Original;
      if (left.Length > 70) left = left.Substring(0, 70) + "…";
      string right = Spoken == null ? "" : Spoken;
      if (right.Length > 70) right = right.Substring(0, 70) + "…";
      if (right.Length == 0 || right == left) return When + "  " + left;
      return When + "  " + left + "  →  " + right;
    }
  }

  public class PickForm : Form
  {
    public Rectangle Chosen;
    public bool Soft;
    Point start;
    Point end;
    bool drag;
    byte[] sheet;
    int sheetW;
    int sheetH;

    public PickForm()
    {
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = false;
      TopMost = true;
      Cursor = Cursors.Cross;
      Bounds = SystemInformation.VirtualScreen;
      KeyPreview = true;
    }

    protected override CreateParams CreateParams
    {
      get
      {
        CreateParams cp = base.CreateParams;
        cp.ExStyle = cp.ExStyle | 0x00080000 | 0x00000008 | 0x00000080;
        if (Soft) cp.ExStyle = cp.ExStyle | 0x08000000;
        return cp;
      }
    }

    protected override void OnShown(EventArgs e)
    {
      base.OnShown(e);
      Bounds = SystemInformation.VirtualScreen;
      Present();
      if (!Soft) Activate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
      drag = true;
      start = e.Location;
      end = e.Location;
      Present();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
      if (!drag) return;
      end = e.Location;
      Present();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
      if (!drag) return;
      drag = false;
      end = e.Location;
      Rectangle local = Norm(start, end);
      if (local.Width < 8 || local.Height < 8)
      {
        DialogResult = DialogResult.Cancel;
        Close();
        return;
      }
      Chosen = new Rectangle(PointToScreen(local.Location), local.Size);
      DialogResult = DialogResult.OK;
      Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
      if (e.KeyCode == Keys.Escape)
      {
        DialogResult = DialogResult.Cancel;
        Close();
      }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    void Present()
    {
      if (!IsHandleCreated || Width < 2 || Height < 2) return;
      int w = Width;
      int h = Height;
      int stride = w * 4;
      if (sheet == null || sheetW != w || sheetH != h) sheet = new byte[stride * h];
      sheetW = w;
      sheetH = h;
      Rectangle hole = drag ? Norm(start, end) : Rectangle.Empty;
      int y;
      for (y = 0; y < h; y++)
      {
        int row = y * stride;
        int x;
        for (x = 0; x < w; x++)
        {
          int i = row + x * 4;
          if (drag && Dash(x, y, hole))
          {
            sheet[i] = 60;
            sheet[i + 1] = 122;
            sheet[i + 2] = 255;
            sheet[i + 3] = 255;
          }
          else if (drag && x >= hole.X && x < hole.Right && y >= hole.Y && y < hole.Bottom)
          {
            sheet[i] = 0;
            sheet[i + 1] = 0;
            sheet[i + 2] = 0;
            sheet[i + 3] = 0;
          }
          else
          {
            sheet[i] = 0;
            sheet[i + 1] = 0;
            sheet[i + 2] = 0;
            sheet[i + 3] = 150;
          }
        }
      }
      PaintHint();
      Push(w, h, stride);
    }

    static bool Dash(int x, int y, Rectangle hole)
    {
      if (hole.Width < 2 || hole.Height < 2) return false;
      int thick = 3;
      bool horizontal = (y >= hole.Y - thick && y < hole.Y && x >= hole.X - thick && x < hole.Right + thick)
        || (y >= hole.Bottom && y < hole.Bottom + thick && x >= hole.X - thick && x < hole.Right + thick);
      bool vertical = (x >= hole.X - thick && x < hole.X && y >= hole.Y - thick && y < hole.Bottom + thick)
        || (x >= hole.Right && x < hole.Right + thick && y >= hole.Y - thick && y < hole.Bottom + thick);
      if (!horizontal && !vertical) return false;
      int along = horizontal ? x - hole.X : y - hole.Y;
      if (along < 0) along = -along;
      return (along % 14) < 8;
    }

    void PaintHint()
    {
      using (Bitmap label = new Bitmap(560, 42, PixelFormat.Format32bppArgb))
      using (Graphics g = Graphics.FromImage(label))
      using (Font font = new Font("Georgia", 16f, FontStyle.Bold))
      {
        g.Clear(Color.Black);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.DrawString("Обведи текст. Esc — отмена.", font, Brushes.White, 8, 6);
        BitmapData data = label.LockBits(new Rectangle(0, 0, label.Width, label.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
          int stride = data.Stride;
          byte[] pix = new byte[stride * label.Height];
          Marshal.Copy(data.Scan0, pix, 0, pix.Length);
          int y;
          for (y = 0; y < label.Height; y++)
          {
            if (24 + y >= sheetH) break;
            int x;
            for (x = 0; x < label.Width; x++)
            {
              if (24 + x >= sheetW) break;
              int s = y * stride + x * 4;
              int bright = pix[s] + pix[s + 1] + pix[s + 2];
              int d = (24 + y) * sheetW * 4 + (24 + x) * 4;
              if (bright > 40)
              {
                sheet[d] = 230;
                sheet[d + 1] = 244;
                sheet[d + 2] = 255;
                sheet[d + 3] = 255;
              }
              else
              {
                sheet[d] = 8;
                sheet[d + 1] = 12;
                sheet[d + 2] = 20;
                sheet[d + 3] = 230;
              }
            }
          }
        }
        finally
        {
          label.UnlockBits(data);
        }
      }
    }

    void Push(int w, int h, int stride)
    {
      DibInfo info = new DibInfo();
      info.Header.Size = 40;
      info.Header.Width = w;
      info.Header.Height = -h;
      info.Header.Planes = 1;
      info.Header.BitCount = 32;
      IntPtr bits;
      IntPtr screen = GetDC(IntPtr.Zero);
      IntPtr dib = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
      IntPtr mem = CreateCompatibleDC(screen);
      try
      {
        if (dib == IntPtr.Zero || bits == IntPtr.Zero) return;
        Marshal.Copy(sheet, 0, bits, stride * h);
        IntPtr old = SelectObject(mem, dib);
        DibSize size = new DibSize();
        size.Cx = w;
        size.Cy = h;
        DibPoint zero = new DibPoint();
        DibPoint where = new DibPoint();
        where.X = Left;
        where.Y = Top;
        DibBlend blend = new DibBlend();
        blend.Alpha = 255;
        blend.Format = 1;
        UpdateLayeredWindow(Handle, screen, ref where, ref size, mem, ref zero, 0, ref blend, 2);
        SelectObject(mem, old);
      }
      finally
      {
        DeleteDC(mem);
        if (dib != IntPtr.Zero) DeleteObject(dib);
        ReleaseDC(IntPtr.Zero, screen);
      }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DibInfo
    {
      public DibHeader Header;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DibHeader
    {
      public int Size;
      public int Width;
      public int Height;
      public short Planes;
      public short BitCount;
      public int Compression;
      public int ImageSize;
      public int XPpm;
      public int YPpm;
      public int Colors;
      public int Important;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DibPoint
    {
      public int X;
      public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DibSize
    {
      public int Cx;
      public int Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DibBlend
    {
      public byte Op;
      public byte Flags;
      public byte Alpha;
      public byte Format;
    }

    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow")]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref DibPoint pptDst, ref DibSize psize, IntPtr hdcSrc, ref DibPoint pptSrc, int crKey, ref DibBlend pblend, int dwFlags);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref DibInfo pbmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    static Rectangle Norm(Point a, Point b)
    {
      int x = Math.Min(a.X, b.X);
      int y = Math.Min(a.Y, b.Y);
      return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }
  }

  public class GameSlip : Form
  {
    public bool Soft;
    public string ToSay;
    public string VoiceText;
    string sourceText;
    string ruText;
    bool showRu;
    bool busy;
    Point dragAt;
    bool moving;
    TextBox box;
    Label title;
    Button swapBtn;
    Button sayBtn;

    public GameSlip(string original, string spoken, bool translated, Rectangle place)
    {
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = false;
      TopMost = true;
      StartPosition = FormStartPosition.Manual;
      BackColor = Color.FromArgb(16, 14, 12);
      ForeColor = Color.FromArgb(246, 239, 228);
      int width = 560;
      int height = 320;
      Screen screen = Screen.FromPoint(new Point(place.X + place.Width / 2, place.Y + place.Height / 2));
      Rectangle area = screen.WorkingArea;
      int x = place.X;
      int y = place.Y;
      if (x + width > area.Right) x = area.Right - width;
      if (y + height > area.Bottom) y = area.Bottom - height;
      if (x < area.Left) x = area.Left;
      if (y < area.Top) y = area.Top;
      Bounds = new Rectangle(x, y, width, height);
      sourceText = original == null ? "" : original;
      ruText = spoken == null ? "" : spoken;
      showRu = translated && ruText.Length > 0;
      ToSay = showRu ? ruText : sourceText;
      VoiceText = ToSay;
      title = new Label();
      title.Text = showRu ? "Перевод  ·  тащи за эту строку" : "Текст  ·  тащи за эту строку";
      title.ForeColor = Color.FromArgb(231, 196, 138);
      title.Font = new Font("Georgia", 11f, FontStyle.Bold);
      title.Cursor = Cursors.SizeAll;
      title.SetBounds(14, 8, width - 56, 22);
      title.MouseDown += delegate(object sender, MouseEventArgs e)
      {
        if (e.Button != MouseButtons.Left) return;
        moving = true;
        dragAt = e.Location;
      };
      title.MouseMove += delegate(object sender, MouseEventArgs e)
      {
        if (!moving || e.Button != MouseButtons.Left) return;
        Point screenAt = title.PointToScreen(e.Location);
        Location = new Point(screenAt.X - dragAt.X, screenAt.Y - dragAt.Y);
      };
      title.MouseUp += delegate { moving = false; };
      Button close = new Button();
      close.Text = "×";
      close.FlatStyle = FlatStyle.Flat;
      close.FlatAppearance.BorderSize = 0;
      close.BackColor = Color.FromArgb(16, 14, 12);
      close.ForeColor = Color.FromArgb(183, 170, 154);
      close.SetBounds(width - 40, 6, 32, 28);
      close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
      box = new TextBox();
      box.Multiline = true;
      box.ReadOnly = false;
      box.BorderStyle = BorderStyle.None;
      box.BackColor = Color.FromArgb(36, 25, 16);
      box.ForeColor = Color.FromArgb(246, 239, 228);
      box.Font = new Font("Segoe UI", 11f);
      box.ScrollBars = ScrollBars.Vertical;
      box.Text = showRu ? ruText : sourceText;
      box.SetBounds(14, 36, width - 28, 210);
      sayBtn = new Button();
      sayBtn.Text = "Говорить";
      sayBtn.FlatStyle = FlatStyle.Flat;
      sayBtn.FlatAppearance.BorderSize = 0;
      sayBtn.BackColor = Color.FromArgb(255, 122, 60);
      sayBtn.ForeColor = Color.FromArgb(26, 13, 7);
      sayBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      sayBtn.SetBounds(14, height - 52, 120, 34);
      sayBtn.Click += delegate { SpeakClick(); };
      swapBtn = new Button();
      swapBtn.Text = showRu ? "Оригинал" : "Перевод";
      swapBtn.FlatStyle = FlatStyle.Flat;
      swapBtn.FlatAppearance.BorderColor = Color.FromArgb(255, 122, 60);
      swapBtn.BackColor = Color.FromArgb(16, 14, 12);
      swapBtn.ForeColor = Color.FromArgb(231, 196, 138);
      swapBtn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      swapBtn.SetBounds(142, height - 52, 120, 34);
      swapBtn.Visible = translated;
      swapBtn.Click += delegate { ToggleText(); };
      Button paste = FlatButton("Вставить");
      paste.SetBounds(270, height - 52, 120, 34);
      paste.Click += delegate { Paste(); };
      Button again = FlatButton("Перевести");
      again.SetBounds(398, height - 52, 140, 34);
      again.Click += delegate { Retranslate(); };
      Controls.Add(title);
      Controls.Add(close);
      Controls.Add(box);
      Controls.Add(sayBtn);
      Controls.Add(swapBtn);
      Controls.Add(paste);
      Controls.Add(again);
      Paint += delegate(object sender, PaintEventArgs e)
      {
        using (Pen pen = new Pen(Color.FromArgb(255, 122, 60), 2f)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
      };
    }

    static Button FlatButton(string text)
    {
      Button button = new Button();
      button.Text = text;
      button.FlatStyle = FlatStyle.Flat;
      button.FlatAppearance.BorderColor = Color.FromArgb(255, 122, 60);
      button.BackColor = Color.FromArgb(16, 14, 12);
      button.ForeColor = Color.FromArgb(231, 196, 138);
      button.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      return button;
    }

    void ToggleText()
    {
      if (busy) return;
      if (showRu) ruText = box.Text == null ? "" : box.Text;
      else sourceText = box.Text == null ? "" : box.Text;
      showRu = !showRu;
      box.Text = showRu ? ruText : sourceText;
      title.Text = (showRu ? "Перевод" : "Оригинал") + "  ·  тащи за эту строку";
      swapBtn.Text = showRu ? "Оригинал" : "Перевод";
      box.SelectionStart = 0;
      box.ScrollToCaret();
    }

    void Paste()
    {
      if (busy) return;
      string clip = ClipText();
      if (clip.Length == 0)
      {
        title.Text = "В буфере пусто";
        return;
      }
      if (clip.Length > 2500) clip = clip.Substring(0, 2500);
      sourceText = clip;
      box.Text = clip;
      showRu = false;
      if (GameOcr.MostlyLatin(clip)) RunTranslate(clip);
      else
      {
        ruText = clip;
        showRu = true;
        ToSay = clip;
        VoiceText = clip;
        swapBtn.Visible = true;
        swapBtn.Text = "Оригинал";
        title.Text = "Текст  ·  тащи за эту строку";
      }
    }

    static string ClipText()
    {
      int n;
      for (n = 0; n < 12; n++)
      {
        string plain = WinClip();
        if (plain != null && plain.Trim().Length > 0) return plain.Trim();
        try
        {
          IDataObject data = Clipboard.GetDataObject();
          if (data != null)
          {
            string text = data.GetData(DataFormats.UnicodeText) as string;
            if (text == null) text = data.GetData(DataFormats.Text) as string;
            if (text != null && text.Trim().Length > 0) return text.Trim();
            string html = data.GetData(DataFormats.Html) as string;
            string fragment = HtmlBits(html);
            if (fragment.Length > 0) return fragment;
          }
        }
        catch
        {
        }
        Thread.Sleep(40);
      }
      return "";
    }

    static string HtmlBits(string html)
    {
      if (html == null) return "";
      int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
      int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
      string body = html;
      if (start >= 0 && end > start) body = html.Substring(start + 20, end - (start + 20));
      body = Regex.Replace(body, "<[^>]+>", " ");
      body = System.Net.WebUtility.HtmlDecode(body);
      return Regex.Replace(body == null ? "" : body, "\\s+", " ").Trim();
    }

    static string WinClip()
    {
      if (!OpenClipboard(IntPtr.Zero)) return "";
      try
      {
        IntPtr handle = GetClipboardData(13);
        if (handle == IntPtr.Zero) handle = GetClipboardData(1);
        if (handle == IntPtr.Zero) return "";
        IntPtr ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return "";
        try
        {
          return GetClipboardData(13) != IntPtr.Zero ? Marshal.PtrToStringUni(ptr) : Marshal.PtrToStringAnsi(ptr);
        }
        finally
        {
          GlobalUnlock(handle);
        }
      }
      finally
      {
        CloseClipboard();
      }
    }

    [DllImport("user32.dll")]
    static extern bool OpenClipboard(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(IntPtr handle);

    void Retranslate()
    {
      if (busy) return;
      string text = box.Text == null ? "" : box.Text.Trim();
      if (!showRu) sourceText = text;
      if (showRu && sourceText != null && sourceText.Length > 0) text = sourceText;
      if (text.Length == 0) return;
      RunTranslate(text);
    }

    void RunTranslate(string text)
    {
      busy = true;
      sayBtn.Enabled = false;
      title.Text = "Перевожу…";
      Thread thread = new Thread(new ThreadStart(delegate
      {
        string ru = "";
        string error = "";
        try
        {
          ru = GameOcr.Translate(text);
        }
        catch (Exception ex)
        {
          error = ex.Message;
        }
        try
        {
          BeginInvoke(new Action(delegate
          {
            busy = false;
            sayBtn.Enabled = true;
            if (ru == null || ru.Length == 0)
            {
              title.Text = error.Length > 0 ? error : "Не перевела";
              box.Text = text;
              showRu = false;
              return;
            }
            sourceText = text;
            ruText = ru;
            showRu = true;
            box.Text = ru;
            VoiceText = ru;
            ToSay = ru;
            swapBtn.Visible = true;
            swapBtn.Text = "Оригинал";
            title.Text = "Перевод  ·  тащи за эту строку";
          }));
        }
        catch
        {
        }
      }));
      thread.IsBackground = true;
      thread.Start();
    }

    void SpeakClick()
    {
      if (busy) return;
      string current = box.Text == null ? "" : box.Text.Trim();
      if (current.Length == 0) return;
      if (!showRu && GameOcr.MostlyLatin(current))
      {
        sourceText = current;
        busy = true;
        sayBtn.Enabled = false;
        Thread thread = new Thread(new ThreadStart(delegate
        {
          string ru = current;
          try { ru = GameOcr.Translate(current); } catch { }
          try
          {
            BeginInvoke(new Action(delegate
            {
              ToSay = ru;
              VoiceText = ru;
              DialogResult = DialogResult.OK;
              Close();
            }));
          }
          catch
          {
          }
        }));
        thread.IsBackground = true;
        thread.Start();
        return;
      }
      if (!showRu && ruText != null && ruText.Length > 0 && current == sourceText) current = ruText;
      ToSay = current;
      VoiceText = current;
      DialogResult = DialogResult.OK;
      Close();
    }

    protected override CreateParams CreateParams
    {
      get
      {
        CreateParams cp = base.CreateParams;
        cp.ExStyle = cp.ExStyle | 0x00000008 | 0x00000080;
        if (Soft) cp.ExStyle = cp.ExStyle | 0x08000000;
        return cp;
      }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
      if (keyData == (Keys.Control | Keys.V))
      {
        Paste();
        return true;
      }
      if (keyData == Keys.Escape)
      {
        DialogResult = DialogResult.Cancel;
        Close();
        return true;
      }
      return base.ProcessCmdKey(ref msg, keyData);
    }
  }

  public class ShotPiece
  {
    public string Text;
    public string Ru;
    public Rectangle Box;
  }

  public class ShotSlip : Form
  {
    public bool Soft;
    public string ToSay;
    Bitmap raw;
    Bitmap painted;
    bool showPaint;
    bool busy;
    List<ShotPiece> pieces;
    PictureBox view;
    Point dragAt;
    bool moving;
    Label title;
    Button swap;
    Button translateButton;

    public ShotSlip(Bitmap original, Bitmap card, string say, bool translated, Rectangle place, List<ShotPiece> pieces)
    {
      this.pieces = pieces;
      raw = new Bitmap(original);
      painted = card == null ? new Bitmap(original) : new Bitmap(card);
      showPaint = translated && card != null;
      FormBorderStyle = FormBorderStyle.None;
      ShowInTaskbar = false;
      TopMost = true;
      StartPosition = FormStartPosition.Manual;
      BackColor = Color.FromArgb(16, 14, 12);
      int width = Math.Min(980, Math.Max(460, raw.Width + 28));
      int height = Math.Min(760, Math.Max(320, raw.Height + 96));
      Screen screen = Screen.FromPoint(new Point(place.X + place.Width / 2, place.Y + place.Height / 2));
      Rectangle area = screen.WorkingArea;
      int x = place.X;
      int y = place.Y;
      if (x + width > area.Right) x = area.Right - width;
      if (y + height > area.Bottom) y = area.Bottom - height;
      if (x < area.Left) x = area.Left;
      if (y < area.Top) y = area.Top;
      Bounds = new Rectangle(x, y, width, height);
      title = new Label();
      title.Text = showPaint ? "Перевод на скриншоте  ·  тащи за эту строку" : "Скриншот  ·  тащи за эту строку";
      title.ForeColor = Color.FromArgb(231, 196, 138);
      title.Font = new Font("Georgia", 11f, FontStyle.Bold);
      title.Cursor = Cursors.SizeAll;
      title.SetBounds(14, 8, width - 56, 22);
      title.MouseDown += delegate(object sender, MouseEventArgs e)
      {
        if (e.Button != MouseButtons.Left) return;
        moving = true;
        dragAt = e.Location;
      };
      title.MouseMove += delegate(object sender, MouseEventArgs e)
      {
        if (!moving || e.Button != MouseButtons.Left) return;
        Point screenAt = title.PointToScreen(e.Location);
        Location = new Point(screenAt.X - dragAt.X, screenAt.Y - dragAt.Y);
      };
      title.MouseUp += delegate { moving = false; };
      Button close = new Button();
      close.Text = "×";
      close.FlatStyle = FlatStyle.Flat;
      close.FlatAppearance.BorderSize = 0;
      close.BackColor = Color.FromArgb(16, 14, 12);
      close.ForeColor = Color.FromArgb(183, 170, 154);
      close.SetBounds(width - 40, 6, 32, 28);
      close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
      view = new PictureBox();
      view.SizeMode = PictureBoxSizeMode.Zoom;
      view.BackColor = Color.FromArgb(8, 10, 14);
      view.Image = showPaint ? painted : raw;
      view.SetBounds(14, 36, width - 28, height - 96);
      Button sayButton = new Button();
      sayButton.Text = "Говорить";
      sayButton.FlatStyle = FlatStyle.Flat;
      sayButton.FlatAppearance.BorderSize = 0;
      sayButton.BackColor = Color.FromArgb(255, 122, 60);
      sayButton.ForeColor = Color.FromArgb(26, 13, 7);
      sayButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      sayButton.SetBounds(14, height - 48, 140, 34);
      sayButton.Click += delegate
      {
        if (busy) return;
        DialogResult = DialogResult.OK;
        Close();
      };
      translateButton = new Button();
      translateButton.Text = "Перевести";
      translateButton.FlatStyle = FlatStyle.Flat;
      translateButton.FlatAppearance.BorderSize = 0;
      translateButton.BackColor = Color.FromArgb(255, 122, 60);
      translateButton.ForeColor = Color.FromArgb(26, 13, 7);
      translateButton.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      translateButton.SetBounds(164, height - 48, 140, 34);
      translateButton.Visible = !showPaint && pieces != null && pieces.Count > 0;
      translateButton.Click += delegate { StartTranslate(); };
      swap = new Button();
      swap.Text = showPaint ? "Оригинал" : "Перевод";
      swap.FlatStyle = FlatStyle.Flat;
      swap.FlatAppearance.BorderColor = Color.FromArgb(255, 122, 60);
      swap.BackColor = Color.FromArgb(16, 14, 12);
      swap.ForeColor = Color.FromArgb(231, 196, 138);
      swap.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
      swap.SetBounds(164, height - 48, 140, 34);
      swap.Visible = showPaint;
      swap.Click += delegate
      {
        showPaint = !showPaint;
        view.Image = showPaint ? painted : raw;
        title.Text = (showPaint ? "Перевод на скриншоте" : "Скриншот") + "  ·  тащи за эту строку";
        swap.Text = showPaint ? "Оригинал" : "Перевод";
      };
      Controls.Add(title);
      Controls.Add(close);
      Controls.Add(view);
      Controls.Add(sayButton);
      Controls.Add(translateButton);
      Controls.Add(swap);
      Paint += delegate(object sender, PaintEventArgs e)
      {
        using (Pen pen = new Pen(Color.FromArgb(255, 122, 60), 2f)) e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
      };
      FormClosed += delegate
      {
        if (view != null) view.Image = null;
        if (raw != null) raw.Dispose();
        if (painted != null) painted.Dispose();
      };
    }

    protected override CreateParams CreateParams
    {
      get
      {
        CreateParams cp = base.CreateParams;
        cp.ExStyle = cp.ExStyle | 0x00000008 | 0x00000080;
        if (Soft) cp.ExStyle = cp.ExStyle | 0x08000000;
        return cp;
      }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
      if (keyData == Keys.Escape)
      {
        DialogResult = DialogResult.Cancel;
        Close();
        return true;
      }
      return base.ProcessCmdKey(ref msg, keyData);
    }

    void StartTranslate()
    {
      if (busy || pieces == null || pieces.Count == 0) return;
      busy = true;
      translateButton.Enabled = false;
      translateButton.Text = "…";
      title.Text = "Перевожу…";
      List<ShotPiece> copy = pieces;
      Thread worker = new Thread(new ThreadStart(delegate
      {
        int n;
        for (n = 0; n < copy.Count; n++)
        {
          ShotPiece piece = copy[n];
          if (piece.Ru != null && piece.Ru.Length > 0 && piece.Ru != piece.Text) continue;
          piece.Ru = piece.Text;
          if (!GameOcr.MostlyLatin(piece.Text)) continue;
          try
          {
            string next = GameOcr.Translate(piece.Text);
            if (next != null && next.Trim().Length > 0) piece.Ru = next.Trim();
          }
          catch
          {
          }
        }
        string spoken = GameOcr.JoinPieces(copy, true);
        Bitmap nextCard = null;
        try { nextCard = GameOcr.Compose(raw, copy, true); }
        catch { }
        if (IsDisposed) return;
        BeginInvoke(new Action(delegate
        {
          if (IsDisposed) return;
          if (nextCard != null)
          {
            if (painted != null && !object.ReferenceEquals(painted, raw)) painted.Dispose();
            painted = nextCard;
            showPaint = true;
            view.Image = painted;
          }
          ToSay = spoken;
          title.Text = "Перевод на скриншоте  ·  тащи за эту строку";
          translateButton.Visible = false;
          swap.Visible = true;
          swap.Text = "Оригинал";
          busy = false;
        }));
      }));
      worker.IsBackground = true;
      worker.Name = "RechShotTranslate";
      worker.Start();
    }
  }

  public class GameOcr
  {
    public static bool MostlyLatin(string text)
    {
      int latin = 0;
      int cyr = 0;
      if (text == null) return false;
      foreach (char c in text)
      {
        if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') latin++;
        else if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё') cyr++;
      }
      return latin > cyr && latin > 2;
    }

    static readonly object gate = new object();

    public static string Read(Bitmap source)
    {
      lock (gate)
      {
        Bitmap plain = Scale(source);
        Bitmap clean = null;
        try
        {
          clean = Clean(source);
          string sharp = Look(clean);
          string raw = Look(plain);
          if (sharp == null && raw == null) throw new Exception("В Windows нет распознавания текста. Добавь русский и английский язык.");
          return Better(sharp, raw);
        }
        finally
        {
          if (clean != null) clean.Dispose();
          if (!object.ReferenceEquals(plain, source)) plain.Dispose();
        }
      }
    }

    static Bitmap Scale(Bitmap source)
    {
      if (source.Width >= 900 && source.Height >= 80) return source;
      int scale = source.Width < 450 ? 3 : 2;
      int w = Math.Max(1, source.Width * scale);
      int h = Math.Max(1, source.Height * scale);
      Bitmap scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb);
      using (Graphics g = Graphics.FromImage(scaled))
      {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, w, h);
      }
      return scaled;
    }

    static Bitmap Clean(Bitmap source)
    {
      int scale = 1;
      if (source.Width < 420) scale = 3;
      else if (source.Width < 1100) scale = 2;
      int w = Math.Max(1, source.Width * scale);
      int h = Math.Max(1, source.Height * scale);
      int pad = 28;
      Bitmap bmp = new Bitmap(w + pad * 2, h + pad * 2, PixelFormat.Format32bppArgb);
      using (Graphics g = Graphics.FromImage(bmp))
      {
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(pad, pad, w, h));
      }
      BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
      try
      {
        int stride = data.Stride;
        int bytes = stride * bmp.Height;
        byte[] pix = new byte[bytes];
        Marshal.Copy(data.Scan0, pix, 0, bytes);
        int n = bmp.Width * bmp.Height;
        long sum = 0;
        int y;
        for (y = 0; y < bmp.Height; y++)
        {
          int row = y * stride;
          int x;
          for (x = 0; x < bmp.Width; x++)
          {
            int i = row + x * 4;
            int lum = (pix[i] * 11 + pix[i + 1] * 59 + pix[i + 2] * 30) / 100;
            if (lum > 255) lum = 255;
            pix[i] = pix[i + 1] = pix[i + 2] = (byte)lum;
            sum += lum;
          }
        }
        if (n > 0 && sum / n < 128)
        {
          for (y = 0; y < bmp.Height; y++)
          {
            int row = y * stride;
            int x;
            for (x = 0; x < bmp.Width; x++)
            {
              int i = row + x * 4;
              byte v = (byte)(255 - pix[i]);
              pix[i] = pix[i + 1] = pix[i + 2] = v;
            }
          }
        }
        int[] hist = new int[256];
        for (y = 0; y < bmp.Height; y++)
        {
          int row = y * stride;
          int x;
          for (x = 0; x < bmp.Width; x++) hist[pix[row + x * 4]]++;
        }
        int low = 0;
        int high = 255;
        int seen = 0;
        int cut = Math.Max(1, n / 40);
        int t;
        for (t = 0; t < 256; t++)
        {
          seen += hist[t];
          if (seen >= cut) { low = t; break; }
        }
        seen = 0;
        for (t = 255; t >= 0; t--)
        {
          seen += hist[t];
          if (seen >= cut) { high = t; break; }
        }
        if (high > low + 8)
        {
          for (y = 0; y < bmp.Height; y++)
          {
            int row = y * stride;
            int x;
            for (x = 0; x < bmp.Width; x++)
            {
              int p = row + x * 4;
              int v = (pix[p] - low) * 255 / (high - low);
              if (v < 0) v = 0;
              if (v > 255) v = 255;
              pix[p] = pix[p + 1] = pix[p + 2] = (byte)v;
            }
          }
        }
        Marshal.Copy(pix, 0, data.Scan0, bytes);
      }
      finally
      {
        bmp.UnlockBits(data);
      }
      return bmp;
    }

    static string Look(Bitmap bitmap)
    {
      string ru = Once(bitmap, "ru");
      string en = Once(bitmap, "en-US");
      if ((ru == null || ru.Length == 0) && (en == null || en.Length == 0))
      {
        Windows.Media.Ocr.OcrEngine any = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
        if (any == null) return ru == null && en == null ? null : "";
        return OnceEngine(bitmap, any);
      }
      if (ru == null || ru.Length == 0) return en == null ? "" : en;
      if (en == null || en.Length == 0) return ru;
      int ruScore = Score(ru);
      int enScore = Score(en);
      if (ruScore == enScore) return MostlyLatin(en) && en.Length >= ru.Length ? en : (ru.Length >= en.Length ? ru : en);
      return enScore > ruScore ? en : ru;
    }

    static string Once(Bitmap bitmap, string tag)
    {
      try
      {
        Windows.Media.Ocr.OcrEngine engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
        if (engine == null) return null;
        return OnceEngine(bitmap, engine);
      }
      catch
      {
        return null;
      }
    }

    static string OnceEngine(Bitmap bitmap, Windows.Media.Ocr.OcrEngine engine)
    {
      Windows.Graphics.Imaging.SoftwareBitmap soft = null;
      Windows.Graphics.Imaging.SoftwareBitmap ready = null;
      try
      {
        soft = ToSoft(bitmap);
        ready = soft;
        if (soft.BitmapAlphaMode != Windows.Graphics.Imaging.BitmapAlphaMode.Ignore || soft.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
        {
          ready = Windows.Graphics.Imaging.SoftwareBitmap.Convert(soft, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
        }
        Windows.Media.Ocr.OcrResult result = engine.RecognizeAsync(ready).AsTask().GetAwaiter().GetResult();
        return result == null || result.Text == null ? "" : result.Text.Trim();
      }
      finally
      {
        if (ready != null && !object.ReferenceEquals(ready, soft)) ready.Dispose();
        if (soft != null) soft.Dispose();
      }
    }

    static string Better(string clean, string plain)
    {
      if (clean == null) clean = "";
      if (plain == null) plain = "";
      clean = clean.Trim();
      plain = plain.Trim();
      if (clean.Length == 0) return plain;
      if (plain.Length == 0) return clean;
      int sharp = Score(clean);
      int raw = Score(plain);
      if (sharp > raw + 12) return clean;
      return raw >= sharp ? plain : clean;
    }

    static int Score(string text)
    {
      if (text == null || text.Length == 0) return 0;
      string[] words = text.Split(new char[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
      int score = 0;
      int w;
      for (w = 0; w < words.Length; w++)
      {
        string word = words[w];
        int latin = 0;
        int cyr = 0;
        int digits = 0;
        int junk = 0;
        int flips = 0;
        bool have = false;
        bool upper = false;
        int i;
        for (i = 0; i < word.Length; i++)
        {
          char c = word[i];
          bool letter = false;
          bool up = false;
          if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') { latin++; letter = true; up = c <= 'Z'; }
          else if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё') { cyr++; letter = true; up = (c >= 'А' && c <= 'Я') || c == 'Ё'; }
          else if (c >= '0' && c <= '9') digits++;
          else if (c != '.' && c != ',' && c != '!' && c != '?' && c != ':' && c != ';' && c != '-' && c != '—' && c != '«' && c != '»' && c != '"' && c != '\'' && c != '(' && c != ')') junk++;
          if (letter)
          {
            if (have && up != upper) flips++;
            upper = up;
            have = true;
          }
        }
        int letters = latin + cyr;
        if (letters == 0)
        {
          score -= 3;
          continue;
        }
        if (latin > 0 && cyr > 0) score -= 10;
        else if (letters >= 2 && letters <= 16) score += letters + 2;
        else score += 1;
        if (digits > 0 && letters > 0) score -= 7;
        if (junk > 0) score -= 8 * junk;
        if (flips > 2) score -= 5;
      }
      return score;
    }

    static Windows.Graphics.Imaging.SoftwareBitmap ToSoft(Bitmap bitmap)
    {
      using (MemoryStream ms = new MemoryStream())
      {
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        Windows.Storage.Streams.InMemoryRandomAccessStream ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        try
        {
          Stream win = ras.AsStreamForWrite();
          ms.CopyTo(win);
          win.Flush();
          ras.Seek(0);
          Windows.Graphics.Imaging.BitmapDecoder decoder = Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras).AsTask().GetAwaiter().GetResult();
          return decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
          ras.Dispose();
        }
      }
    }

    public static List<ShotPiece> Layout(Bitmap source)
    {
      if (source == null) return new List<ShotPiece>();
      float scale = 1f;
      Bitmap used = source;
      bool owned = false;
      if (source.Width < 720)
      {
        scale = source.Width < 400 ? 3f : 2f;
        int w = Math.Max(1, (int)(source.Width * scale));
        int h = Math.Max(1, (int)(source.Height * scale));
        used = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(used))
        {
          g.InterpolationMode = InterpolationMode.HighQualityBicubic;
          g.PixelOffsetMode = PixelOffsetMode.HighQuality;
          g.DrawImage(source, 0, 0, w, h);
        }
        owned = true;
      }
      try
      {
        List<RawWord> ru = Collect(used, "ru");
        List<RawWord> en = Collect(used, "en-US");
        List<RawWord> best = PickWords(ru, en);
        return Cluster(best, scale, source.Width, source.Height);
      }
      finally
      {
        if (owned) used.Dispose();
      }
    }

    public static string JoinPieces(List<ShotPiece> pieces, bool ru)
    {
      StringBuilder spoken = new StringBuilder();
      if (pieces == null) return "";
      int i;
      for (i = 0; i < pieces.Count; i++)
      {
        string text = ru ? pieces[i].Ru : pieces[i].Text;
        if (text == null) continue;
        text = text.Trim();
        if (text.Length == 0) continue;
        if (spoken.Length > 0) spoken.Append(". ");
        spoken.Append(text);
      }
      return spoken.ToString();
    }

    public static Bitmap Compose(Bitmap shot, List<ShotPiece> pieces, bool translated)
    {
      Bitmap bmp = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppArgb);
      using (Graphics g = Graphics.FromImage(bmp))
      {
        g.DrawImage(shot, 0, 0, shot.Width, shot.Height);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = SmoothingMode.None;
        if (pieces == null) return bmp;
        int i;
        for (i = 0; i < pieces.Count; i++)
        {
          ShotPiece piece = pieces[i];
          string text = translated ? piece.Ru : piece.Text;
          if (text == null || text.Trim().Length == 0) continue;
          Rectangle box = piece.Box;
          if (box.Width < 2 || box.Height < 2) continue;
          Color bg = Sample(shot, box);
          int lum = (bg.R * 30 + bg.G * 59 + bg.B * 11) / 100;
          Color ink = lum > 150 ? Color.FromArgb(24, 18, 14) : Color.FromArgb(246, 242, 234);
          float size = Math.Max(11f, box.Height * 0.86f);
          Font font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
          SizeF measured = g.MeasureString(text, font);
          while (measured.Width > box.Width + 6 && size > 10f)
          {
            font.Dispose();
            size -= 1f;
            font = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
            measured = g.MeasureString(text, font);
          }
          Rectangle plate = box;
          int need = (int)Math.Ceiling(measured.Width) + 4;
          if (need > plate.Width)
          {
            plate.Width = need;
            if (plate.Right > bmp.Width) plate.Width = Math.Max(8, bmp.Width - plate.X);
          }
          plate.Inflate(1, 1);
          if (plate.X < 0) plate.X = 0;
          if (plate.Y < 0) plate.Y = 0;
          if (plate.Right > bmp.Width) plate.Width = bmp.Width - plate.X;
          if (plate.Bottom > bmp.Height) plate.Height = bmp.Height - plate.Y;
          using (SolidBrush back = new SolidBrush(bg)) g.FillRectangle(back, plate);
          using (SolidBrush brush = new SolidBrush(ink))
          {
            RectangleF spot = new RectangleF(plate.X + 1, plate.Y, Math.Max(1, plate.Width - 2), Math.Max(1, plate.Height));
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Center;
            format.Trimming = StringTrimming.EllipsisCharacter;
            format.FormatFlags = StringFormatFlags.NoWrap;
            g.DrawString(text, font, brush, spot, format);
            format.Dispose();
          }
          font.Dispose();
        }
      }
      return bmp;
    }

    class RawWord
    {
      public string Text;
      public int X;
      public int Y;
      public int W;
      public int H;
    }

    static List<RawWord> Collect(Bitmap bitmap, string tag)
    {
      List<RawWord> list = new List<RawWord>();
      Windows.Graphics.Imaging.SoftwareBitmap soft = null;
      Windows.Graphics.Imaging.SoftwareBitmap ready = null;
      try
      {
        Windows.Media.Ocr.OcrEngine engine = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
        if (engine == null) return list;
        soft = ToSoft(bitmap);
        ready = soft;
        if (soft.BitmapAlphaMode != Windows.Graphics.Imaging.BitmapAlphaMode.Ignore || soft.BitmapPixelFormat != Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8)
        {
          ready = Windows.Graphics.Imaging.SoftwareBitmap.Convert(soft, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
        }
        Windows.Media.Ocr.OcrResult result = engine.RecognizeAsync(ready).AsTask().GetAwaiter().GetResult();
        if (result == null || result.Lines == null) return list;
        foreach (Windows.Media.Ocr.OcrLine line in result.Lines)
        {
          if (line.Words == null) continue;
          foreach (Windows.Media.Ocr.OcrWord word in line.Words)
          {
            if (word.Text == null || word.Text.Length == 0) continue;
            Windows.Foundation.Rect rect = word.BoundingRect;
            RawWord item = new RawWord();
            item.Text = word.Text;
            item.X = (int)rect.X;
            item.Y = (int)rect.Y;
            item.W = Math.Max(1, (int)rect.Width);
            item.H = Math.Max(1, (int)rect.Height);
            list.Add(item);
          }
        }
      }
      catch
      {
      }
      finally
      {
        if (ready != null && !object.ReferenceEquals(ready, soft)) ready.Dispose();
        if (soft != null) soft.Dispose();
      }
      return list;
    }

    static List<RawWord> PickWords(List<RawWord> ru, List<RawWord> en)
    {
      string ruText = JoinRaw(ru);
      string enText = JoinRaw(en);
      if (ru == null || ru.Count == 0) return en == null ? new List<RawWord>() : en;
      if (en == null || en.Count == 0) return ru;
      return Score(enText) > Score(ruText) ? en : ru;
    }

    static string JoinRaw(List<RawWord> words)
    {
      if (words == null) return "";
      StringBuilder line = new StringBuilder();
      int i;
      for (i = 0; i < words.Count; i++)
      {
        if (line.Length > 0) line.Append(' ');
        line.Append(words[i].Text);
      }
      return line.ToString();
    }

    static List<ShotPiece> Cluster(List<RawWord> words, float scale, int maxW, int maxH)
    {
      List<ShotPiece> pieces = new List<ShotPiece>();
      if (words == null || words.Count == 0 || scale <= 0) return pieces;
      words.Sort(delegate(RawWord a, RawWord b)
      {
        int dy = (a.Y + a.H / 2) - (b.Y + b.H / 2);
        if (dy > 4 || dy < -4) return dy;
        return a.X - b.X;
      });
      List<List<RawWord>> lines = new List<List<RawWord>>();
      int n;
      for (n = 0; n < words.Count; n++)
      {
        RawWord word = words[n];
        List<RawWord> line = null;
        if (lines.Count > 0)
        {
          List<RawWord> last = lines[lines.Count - 1];
          RawWord prev = last[0];
          int tol = Math.Max(8, Math.Max(prev.H, word.H) / 2);
          int dy = (word.Y + word.H / 2) - (prev.Y + prev.H / 2);
          if (dy < 0) dy = -dy;
          if (dy <= tol) line = last;
        }
        if (line == null)
        {
          line = new List<RawWord>();
          lines.Add(line);
        }
        line.Add(word);
      }
      int li;
      for (li = 0; li < lines.Count; li++)
      {
        List<RawWord> line = lines[li];
        line.Sort(delegate(RawWord a, RawWord b) { return a.X - b.X; });
        int start = 0;
        int i;
        for (i = 1; i <= line.Count; i++)
        {
          bool split = i == line.Count;
          if (!split)
          {
            RawWord prev = line[i - 1];
            RawWord cur = line[i];
            int gap = cur.X - (prev.X + prev.W);
            if (gap > Math.Max(22, prev.H)) split = true;
          }
          if (!split) continue;
          StringBuilder sb = new StringBuilder();
          int x0 = int.MaxValue;
          int y0 = int.MaxValue;
          int x1 = 0;
          int y1 = 0;
          int k;
          for (k = start; k < i; k++)
          {
            RawWord word = line[k];
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(word.Text);
            if (word.X < x0) x0 = word.X;
            if (word.Y < y0) y0 = word.Y;
            if (word.X + word.W > x1) x1 = word.X + word.W;
            if (word.Y + word.H > y1) y1 = word.Y + word.H;
          }
          ShotPiece piece = new ShotPiece();
          piece.Text = sb.ToString().Trim();
          int x = (int)(x0 / scale);
          int y = (int)(y0 / scale);
          int w = Math.Max(1, (int)((x1 - x0) / scale));
          int h = Math.Max(1, (int)((y1 - y0) / scale));
          if (x < 0) x = 0;
          if (y < 0) y = 0;
          if (x >= maxW) x = maxW - 1;
          if (y >= maxH) y = maxH - 1;
          if (x + w > maxW) w = maxW - x;
          if (y + h > maxH) h = maxH - y;
          piece.Box = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
          piece.Ru = piece.Text;
          if (piece.Text.Length > 0) pieces.Add(piece);
          start = i;
        }
      }
      return pieces;
    }

    static Color Sample(Bitmap shot, Rectangle box)
    {
      List<int> colors = new List<int>();
      int y = box.Y - 3;
      if (y < 0) y = Math.Min(shot.Height - 1, box.Bottom + 2);
      int x = box.X;
      while (x < box.Right && colors.Count < 24)
      {
        if (x >= 0 && x < shot.Width && y >= 0 && y < shot.Height) colors.Add(shot.GetPixel(x, y).ToArgb());
        x += 4;
      }
      if (colors.Count == 0) return Color.FromArgb(16, 22, 32);
      colors.Sort();
      return Color.FromArgb(colors[colors.Count / 2]);
    }

    public static string Translate(string text)
    {
      if (text == null) return "";
      text = FixOcr(text);
      string[] bits = Regex.Split(text, "(?<=[\\.!\\?])\\s+");
      StringBuilder spoken = new StringBuilder();
      int i;
      for (i = 0; i < bits.Length; i++)
      {
        string chunk = bits[i].Trim();
        if (chunk.Length == 0) continue;
        if (spoken.Length > 0) spoken.Append(' ');
        if (!MostlyLatin(chunk)) spoken.Append(chunk);
        else spoken.Append(TranslateChunk(chunk));
      }
      return spoken.ToString().Trim();
    }

    static string FixOcr(string text)
    {
      text = Regex.Replace(text, "^\\s*english\\s*:\\s*", "", RegexOptions.IgnoreCase);
      return Regex.Replace(text, "(?<=^|[\\s\"«(])1(?=\\s+(?:usually|have|had|am|was|were|took|take|go|went|do|did|can|will|would|think|like|want|need|see|know|got|get|play|say|said|love|live|work|come|came|make|made)\\b)", "I", RegexOptions.IgnoreCase);
    }

    static string TranslateChunk(string text)
    {
      string google = AskGoogle(text);
      if (GoodRu(google)) return google;
      string memory = AskMemory(text);
      if (GoodRu(memory)) return memory;
      if (google != null && google.Length > 0) return google;
      if (memory != null && memory.Length > 0) return memory;
      throw new Exception("Не смогла перевести. Нужен интернет, ключ не нужен.");
    }

    static bool GoodRu(string text)
    {
      if (text == null || text.Length == 0) return false;
      int latin = 0;
      int cyr = 0;
      foreach (char c in text)
      {
        if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z') latin++;
        else if (c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я' || c == 'ё' || c == 'Ё') cyr++;
      }
      return cyr > latin;
    }

    static string AskGoogle(string text)
    {
      try
      {
        string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=ru&dt=t&q=" + Uri.EscapeDataString(text);
        using (HttpClient client = new HttpClient())
        {
          client.Timeout = TimeSpan.FromSeconds(20);
          client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
          string json = client.GetStringAsync(url).GetAwaiter().GetResult();
          JArray root = JArray.Parse(json);
          JArray parts = root[0] as JArray;
          StringBuilder line = new StringBuilder();
          if (parts != null)
          {
            foreach (JToken part in parts)
            {
              JArray row = part as JArray;
              if (row != null && row.Count > 0 && row[0].Type == JTokenType.String) line.Append((string)row[0]);
            }
          }
          return line.ToString().Trim();
        }
      }
      catch
      {
        return "";
      }
    }

    static string AskMemory(string text)
    {
      try
      {
        string url = "https://api.mymemory.translated.net/get?langpair=en|ru&q=" + Uri.EscapeDataString(text);
        using (HttpClient client = new HttpClient())
        {
          client.Timeout = TimeSpan.FromSeconds(20);
          client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
          string json = client.GetStringAsync(url).GetAwaiter().GetResult();
          JObject root = JObject.Parse(json);
          string value = (string)root["responseData"]["translatedText"];
          if (value == null) return "";
          if (value.IndexOf("MYMEMORY WARNING", StringComparison.OrdinalIgnoreCase) >= 0) return "";
          return value.Trim();
        }
      }
      catch
      {
        return "";
      }
    }
  }

  public class ClipWindow : Form
  {
    static ClipWindow open;
    public static string Home = "";
    readonly WebView2 view;
    readonly Label note;
    string shown = "";
    bool starting;

    public static void Sync(string url)
    {
      if (url == null) url = "";
      if (url.Length == 0)
      {
        if (open != null && !open.IsDisposed) open.Apply("");
        return;
      }
      if (open == null || open.IsDisposed) open = new ClipWindow();
      open.Apply(url);
    }

    public ClipWindow()
    {
      Text = "ЛюсиЧат — клип";
      FormBorderStyle = FormBorderStyle.None;
      StartPosition = FormStartPosition.Manual;
      ShowInTaskbar = false;
      ClientSize = new Size(960, 540);
      BackColor = Color.Black;
      Rectangle all = SystemInformation.VirtualScreen;
      Location = new Point(all.X - Width - 80, all.Y + 20);
      view = new WebView2();
      view.Dock = DockStyle.Fill;
      view.DefaultBackgroundColor = Color.Black;
      note = new Label();
      note.Dock = DockStyle.Fill;
      note.ForeColor = Color.White;
      note.BackColor = Color.Black;
      note.TextAlign = ContentAlignment.MiddleCenter;
      note.Visible = false;
      Controls.Add(view);
      Controls.Add(note);
    }

    protected override bool ShowWithoutActivation
    {
      get { return true; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    void Park()
    {
      if (!IsHandleCreated) return;
      SetWindowPos(Handle, new IntPtr(1), -3200, 80, 960, 540, 0x0010);
    }

    void Apply(string url)
    {
      if (url == null) url = "";
      if (url.Length == 0)
      {
        if (shown == "off") return;
        shown = "off";
        try
        {
          if (view.CoreWebView2 != null)
          {
            string off = Home == null ? "" : Home;
            if (off.EndsWith("/player")) off = off + "/off";
            if (off.Length == 0) off = "about:blank";
            view.CoreWebView2.Navigate(off);
          }
        }
        catch
        {
        }
        Park();
        return;
      }
      if (!Visible) Show();
      Park();
      if (shown == "page" && view.CoreWebView2 != null) return;
      shown = "page";
      note.Visible = false;
      Begin();
    }

    async void Begin()
    {
      if (starting) return;
      starting = true;
      try
      {
        if (view.CoreWebView2 == null)
        {
          string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rech", "clip");
          Directory.CreateDirectory(dir);
          CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dir, new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
          await view.EnsureCoreWebView2Async(env);
          view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
          view.CoreWebView2.Settings.IsStatusBarEnabled = false;
          view.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        }
        if (Home != null && Home.Length > 0) view.CoreWebView2.Navigate(Home);
      }
      catch (Exception ex)
      {
        note.Text = "Клип не открылся. Нужен компонент Microsoft Edge WebView2.\n" + ex.Message;
        note.Visible = true;
        note.BringToFront();
      }
      finally
      {
        starting = false;
      }
    }
  }

  public static class ObsEye
  {
    static readonly object gate = new object();
    static bool busy;
    static bool latest;
    static bool have;

    public static void Want(bool on)
    {
      lock (gate)
      {
        if (have && latest == on) return;
        latest = on;
        have = true;
        if (busy) return;
        busy = true;
      }
      ThreadPool.QueueUserWorkItem(delegate { Run(); });
    }

    static void Run()
    {
      while (true)
      {
        bool on;
        lock (gate) on = latest;
        try { Push(on); } catch { }
        lock (gate)
        {
          if (latest == on)
          {
            busy = false;
            return;
          }
        }
      }
    }

    static void Push(bool on)
    {
      int port = 4455;
      string password = "";
      try
      {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");
        if (File.Exists(path))
        {
          JObject cfg = JObject.Parse(File.ReadAllText(path));
          if (cfg["server_port"] != null) port = (int)cfg["server_port"];
          if (cfg["server_password"] != null) password = (string)cfg["server_password"];
          if (cfg["server_enabled"] != null && cfg["server_enabled"].Type == JTokenType.Boolean && !(bool)cfg["server_enabled"]) return;
        }
      }
      catch
      {
      }
      using (ClientWebSocket socket = new ClientWebSocket())
      using (CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
      {
        socket.ConnectAsync(new Uri("ws://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)), cancel.Token).GetAwaiter().GetResult();
        JObject hello = JObject.Parse(Read(socket, cancel.Token));
        JObject identify = new JObject();
        identify["op"] = 1;
        JObject helloData = new JObject();
        helloData["rpcVersion"] = 1;
        JToken auth = hello["d"] == null ? null : hello["d"]["authentication"];
        if (auth != null && auth.Type == JTokenType.Object)
        {
          helloData["authentication"] = Sign(password, (string)auth["salt"], (string)auth["challenge"]);
        }
        identify["d"] = helloData;
        Send(socket, identify.ToString(Formatting.None), cancel.Token);
        Read(socket, cancel.Token);
        JObject inputs = Ask(socket, "GetInputList", null, cancel.Token);
        List<string> names = new List<string>();
        JArray rows = inputs == null ? null : inputs["responseData"]["inputs"] as JArray;
        if (rows != null)
        {
          foreach (JToken row in rows)
          {
            string kind = (string)row["inputKind"];
            string name = (string)row["inputName"];
            if (kind == null || name == null || kind.IndexOf("window", StringComparison.OrdinalIgnoreCase) < 0) continue;
            JObject data = new JObject();
            data["inputName"] = name;
            JObject settings = Ask(socket, "GetInputSettings", data, cancel.Token);
            string window = "";
            try { window = (string)settings["responseData"]["inputSettings"]["window"]; } catch { }
            if (window != null && window.IndexOf("клип", StringComparison.OrdinalIgnoreCase) >= 0) names.Add(name);
          }
        }
        if (names.Count == 0) return;
        JObject scenes = Ask(socket, "GetSceneList", null, cancel.Token);
        JArray sceneRows = scenes == null ? null : scenes["responseData"]["scenes"] as JArray;
        if (sceneRows == null) return;
        foreach (JToken sceneRow in sceneRows)
        {
          string scene = (string)sceneRow["sceneName"];
          if (scene == null || scene.Length == 0) continue;
          JObject sceneData = new JObject();
          sceneData["sceneName"] = scene;
          JObject items = Ask(socket, "GetSceneItemList", sceneData, cancel.Token);
          JArray itemRows = items == null ? null : items["responseData"]["sceneItems"] as JArray;
          if (itemRows == null) continue;
          foreach (JToken item in itemRows)
          {
            string source = (string)item["sourceName"];
            if (source == null || !names.Contains(source)) continue;
            JObject toggle = new JObject();
            toggle["sceneName"] = scene;
            toggle["sceneItemId"] = item["sceneItemId"];
            toggle["sceneItemEnabled"] = on;
            Ask(socket, "SetSceneItemEnabled", toggle, cancel.Token);
          }
        }
      }
    }

    static string Sign(string password, string salt, string challenge)
    {
      using (SHA256 sha = SHA256.Create())
      {
        string secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes((password ?? "") + (salt ?? ""))));
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + (challenge ?? ""))));
      }
    }

    static JObject Ask(ClientWebSocket socket, string type, JObject data, CancellationToken token)
    {
      string id = Guid.NewGuid().ToString("N");
      JObject msg = new JObject();
      msg["op"] = 6;
      JObject body = new JObject();
      body["requestType"] = type;
      body["requestId"] = id;
      if (data != null) body["requestData"] = data;
      msg["d"] = body;
      Send(socket, msg.ToString(Formatting.None), token);
      int i;
      for (i = 0; i < 12; i++)
      {
        JObject root = JObject.Parse(Read(socket, token));
        if ((int)root["op"] != 7) continue;
        if ((string)root["d"]["requestId"] != id) continue;
        return (JObject)root["d"];
      }
      return null;
    }

    static void Send(ClientWebSocket socket, string json, CancellationToken token)
    {
      byte[] raw = Encoding.UTF8.GetBytes(json);
      socket.SendAsync(new ArraySegment<byte>(raw), WebSocketMessageType.Text, true, token).GetAwaiter().GetResult();
    }

    static string Read(ClientWebSocket socket, CancellationToken token)
    {
      byte[] buf = new byte[65536];
      using (MemoryStream stream = new MemoryStream())
      {
        while (true)
        {
          WebSocketReceiveResult result = socket.ReceiveAsync(new ArraySegment<byte>(buf), token).GetAwaiter().GetResult();
          if (result.MessageType == WebSocketMessageType.Close) return "";
          stream.Write(buf, 0, result.Count);
          if (result.EndOfMessage) break;
        }
        return Encoding.UTF8.GetString(stream.ToArray());
      }
    }
  }
}

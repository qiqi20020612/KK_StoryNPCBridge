using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace KK_StoryNPCBridge {
internal static class Json {
 public static Dictionary<string,object> Map(params object[] pairs) {
  var d=new Dictionary<string,object>(); for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]); return d;
 }
 public static object Parse(string s) {var p=new Parser(s); object v=p.Value(0);p.Space();if(p.i!=s.Length)throw new FormatException("Trailing JSON content");return v;}
 public static string Stringify(object v) {var b=new StringBuilder();Write(b,v,0);return b.ToString();}
 static void Write(StringBuilder b,object v,int depth) {
  if(depth>32)throw new FormatException("Nesting limit");
  if(v==null){b.Append("null");return;}
  if(v is string){Quote(b,(string)v);return;}
  if(v is bool){b.Append((bool)v?"true":"false");return;}
  var d=v as IDictionary;
  if(d!=null){b.Append('{');bool first=true;foreach(DictionaryEntry e in d){if(!first)b.Append(',');first=false;Quote(b,(string)e.Key);b.Append(':');Write(b,e.Value,depth+1);}b.Append('}');return;}
  var a=v as IEnumerable;
  if(a!=null){b.Append('[');bool first=true;foreach(object x in a){if(!first)b.Append(',');first=false;Write(b,x,depth+1);}b.Append(']');return;}
  if(v is byte||v is short||v is int||v is long||v is uint||v is ulong||v is float||v is double||v is decimal){double n=Convert.ToDouble(v,CultureInfo.InvariantCulture);if(double.IsNaN(n)||double.IsInfinity(n))throw new FormatException("Nonfinite number");b.Append(Convert.ToString(v,CultureInfo.InvariantCulture));return;}
  throw new FormatException("Unsupported JSON type");
 }
 static void Quote(StringBuilder b,string s) {
  b.Append('"');foreach(char c in s){switch(c){case '"':b.Append("\\\"");break;case '\\':b.Append("\\\\");break;case '\n':b.Append("\\n");break;case '\r':b.Append("\\r");break;case '\t':b.Append("\\t");break;default:if(c<32)b.Append("\\u"+((int)c).ToString("x4"));else b.Append(c);break;}}b.Append('"');
 }
 sealed class Parser {
  readonly string s; public int i; public Parser(string text){s=text;}
  public void Space(){while(i<s.Length&&" \t\r\n".IndexOf(s[i])>=0)i++;}
  char Take(){if(i>=s.Length)throw new FormatException("Unexpected JSON end");return s[i++];}
  void Expect(char c){Space();if(Take()!=c)throw new FormatException("Unexpected JSON token");}
  public object Value(int depth) {
   if(depth>32)throw new FormatException("Nesting limit");Space();if(i>=s.Length)throw new FormatException("Missing value");char c=s[i];
   if(c=='"')return Str();
   if(c=='{'){i++;var d=new Dictionary<string,object>();Space();if(i<s.Length&&s[i]=='}'){i++;return d;}while(true){Space();string k=Str();Expect(':');if(d.ContainsKey(k))throw new FormatException("Duplicate key");d.Add(k,Value(depth+1));Space();c=Take();if(c=='}')return d;if(c!=',')throw new FormatException("Expected comma");}}
   if(c=='['){i++;var a=new List<object>();Space();if(i<s.Length&&s[i]==']'){i++;return a;}while(true){a.Add(Value(depth+1));Space();c=Take();if(c==']')return a;if(c!=',')throw new FormatException("Expected comma");}}
   if(c=='t'){Literal("true");return true;}if(c=='f'){Literal("false");return false;}if(c=='n'){Literal("null");return null;}
   int start=i;if(c=='-')i++;if(i>=s.Length)throw new FormatException("Invalid number");
   if(s[i]=='0')i++;else{if(s[i]<'1'||s[i]>'9')throw new FormatException("Invalid number");Digits();}
   if(i<s.Length&&s[i]=='.'){i++;Digits();}
   if(i<s.Length&&(s[i]=='e'||s[i]=='E')){i++;if(i<s.Length&&(s[i]=='+'||s[i]=='-'))i++;Digits();}
   string n=s.Substring(start,i-start);long whole;if(long.TryParse(n,NumberStyles.Integer,CultureInfo.InvariantCulture,out whole))return whole;double real;
   if(!double.TryParse(n,NumberStyles.Float,CultureInfo.InvariantCulture,out real)||double.IsNaN(real)||double.IsInfinity(real))throw new FormatException("Invalid finite number");return real;
  }
  void Digits(){int start=i;while(i<s.Length&&s[i]>='0'&&s[i]<='9')i++;if(i==start)throw new FormatException("Missing digits");}
  void Literal(string t){foreach(char c in t)if(Take()!=c)throw new FormatException("Invalid literal");}
  string Str(){if(Take()!='"')throw new FormatException("Expected string");var b=new StringBuilder();while(true){char c=Take();if(c=='"')return b.ToString();if(c<32)throw new FormatException("Unescaped control");if(c!='\\'){b.Append(c);continue;}switch(Take()){case '"':b.Append('"');break;case '\\':b.Append('\\');break;case '/':b.Append('/');break;case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;case 'u':int n=0;for(int k=0;k<4;k++){int x="0123456789abcdef".IndexOf(char.ToLowerInvariant(Take()));if(x<0)throw new FormatException("Invalid Unicode");n=n*16+x;}b.Append((char)n);break;default:throw new FormatException("Invalid escape");}}}
 }
}
}

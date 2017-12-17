using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace kubastick_s_Bitcoin_CPU_miner
{

    class Pool
    {
        public Uri Url;
        public string User;
        public string Password = "x";

        public Pool(string pool,string login,string password)
        {
            if (pool.Substring(0,7) == "stratum")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("This bitcoin miner do not support TCP/STRATUM connections!");
                throw new Exception("Invalid url");
            }
            if (pool.Substring(0,7) == "http://")
            {
                Url = new Uri(pool);
            }
            else
            {
                Url = new Uri("http://" + pool);
            }
            User = login;
            Password = password;
        }

        private string InvokeMethod(string method, string paramString = null)
        {
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(Url);
            webRequest.Credentials = new NetworkCredential(User, Password);
            webRequest.ContentType = "application/json-rpc";
            webRequest.Method = "POST";

            string jsonParam = (paramString != null) ? "\"" + paramString + "\"" : "";
            string request = "{\"id\": 0, \"method\": \"" + method + "\", \"params\": [" + jsonParam + "]}";

            // serialize json for the request
            byte[] byteArray = Encoding.UTF8.GetBytes(request);
            webRequest.ContentLength = byteArray.Length;
            using (Stream dataStream = webRequest.GetRequestStream())
                dataStream.Write(byteArray, 0, byteArray.Length);

            string reply = "";
            using (WebResponse webResponse = webRequest.GetResponse())
            using (Stream str = webResponse.GetResponseStream())
            using (StreamReader reader = new StreamReader(str))
                reply = reader.ReadToEnd();

            return reply;
        }

        public Work GetWork()
        {
            Work x = new Work(ParseData(InvokeMethod("getwork")));
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write("Done\n");
            Console.ForegroundColor = ConsoleColor.White;
            return x;
        }

        private byte[] ParseData(string json)
        {
            Match match = Regex.Match(json, "\"data\": \"([A-Fa-f0-9]+)");
            if (match.Success)
            {
                string data = Utils.RemovePadding(match.Groups[1].Value);
                data = Utils.EndianFlip32BitChunks(data);
                return Utils.ToBytes(data);
            }
            throw new Exception("Didn't find valid 'data' in Server Response");
        }

        public bool SendShare(byte[] share)
        {
            string data = Utils.EndianFlip32BitChunks(Utils.ToString(share));
            string paddedData = Utils.AddPadding(data);
            string jsonReply = InvokeMethod("getwork", paddedData);
            Match match = Regex.Match(jsonReply, "\"result\": true");
            return match.Success;
        }
    }

    class Work
    {
        public Work(byte[] data)
        {
            Data = data;
            Current = (byte[])data.Clone();
            _nonceOffset = Data.Length - 4;
            _ticks = DateTime.Now.Ticks;
            _hasher = new SHA256Managed();

        }
        private SHA256Managed _hasher;
        private long _ticks;
        private long _nonceOffset;
        public byte[] Data;
        public byte[] Current;

        internal bool FindShare(ref uint nonce, uint batchSize)
        {
            for (; batchSize > 0; batchSize--)
            {
                BitConverter.GetBytes(nonce).CopyTo(Current, _nonceOffset);
                byte[] doubleHash = Sha256(Sha256(Current));

                //count trailing bytes that are zero
                int zeroBytes = 0;
                for (int i = 31; i >= 28; i--, zeroBytes++)
                    if (doubleHash[i] > 0)
                        break;

                //standard share difficulty matched! (target:ffffffffffffffffffffffffffffffffffffffffffffffffffffffff00000000)
                if (zeroBytes == 4)
                    return true;

                //increase
                if (++nonce == uint.MaxValue)
                    nonce = 0;
            }
            return false;
        }

        private byte[] Sha256(byte[] input)
        {
            byte[] crypto = _hasher.ComputeHash(input, 0, input.Length);
            return crypto;
        }

        public byte[] Hash
        {
            get { return Sha256(Sha256(Current)); }
        }

        public long Age
        {
            get { return DateTime.Now.Ticks - _ticks; }
        }
    }


    class Program
    {
        //Variables
        static Pool _pool = null;
        static Work _work = null;
        static uint _nonce = 0;
        static long _maxAgeTicks = 20000 * TimeSpan.TicksPerMillisecond;
        static uint _batchSize = 100000;
        static int printcounter = 0;

        static void Main(string[] args)
        {
            //Say Hello
            Welcome();
            //Start Miner
            Miner();
        }

        //Hello
        static void Welcome()
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine("===========================================");
            Console.WriteLine("=====Bitcoin CPU miner by Jakub Tomana=====");
            Console.WriteLine("===========================================");
            Console.WriteLine("=================Donations=================");
            Console.WriteLine("=BTC:3FMiVhp7V9VxSbj4wcwfdZ9jSPGKfJQdek====");
            Console.WriteLine("=ETH:0x2ceab97090f749c704aeaba58eeca22529d=");
            Console.WriteLine("=12caa=====================================");
            Console.WriteLine("=XMR:4Ave55zjFgw3inzDBtdHGRgsoQ5Dmj7hVgPvy=");
            Console.WriteLine("=pPViiy3jc4QjpmjcbfWbwaVe4vUMveKAzAiA4j8xg=");
            Console.WriteLine("=Ui29TpKXpm3wzo83H=========================");
            Console.WriteLine("===========================================");
            Console.ForegroundColor = ConsoleColor.White;
        }

        //Main Miner
        static void Miner()
        {
           

            while (true)
            {
                try
                {
                    if(_pool == null)
                        _pool = SelectPool();
                    _work = GetWork();
                    while (true)
                    {
                        if (_work == null || _work.Age > _maxAgeTicks)
                            _work = GetWork();

                        if (_work.FindShare(ref _nonce, _batchSize))
                        {
                            SendShare(_work.Current);
                            _work = null;
                        }
                        else
                            PrintCurrentState();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine();
                    Console.Write("Bitcoin miner has been crashed: ");
                    Console.WriteLine(e.Message);
                }
                Console.WriteLine();
                Console.Write("Hit 'Enter' to try restart...");
                Console.ReadLine();
            }
        }

        //Rest of functions
        private static void ClearConsole()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine("==================================");
            Console.WriteLine("=== kubastick's Bitcoin Miner ====");
            Console.WriteLine("==================================");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine();
        }

        private static Pool SelectPool()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Enter pool adress (with port):");
            string pool = ReadLineDefault("mine.p2pool.com:9332");
            Console.Write("Enter username:");
            string username = ReadLineDefault("3FMiVhp7V9VxSbj4wcwfdZ9jSPGKfJQdek");
            Console.Write("Enter password (usually worker name or x):");
            string password = ReadLineDefault("ImTestingBitcoinMinerDoNotWorry");
            return new Pool(pool,username,password);
        }

        private static Work GetWork()
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write("Requesting Work from Pool...");
            Console.ForegroundColor = ConsoleColor.White;
            return _pool.GetWork();
        }

        private static void SendShare(byte[] share)
        {
            //ClearConsole();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[Succes]Share found!");
            Console.WriteLine("Share: " + Utils.ToString(_work.Current));
            Console.WriteLine("Nonce: " + Utils.ToString(_nonce));
            Console.WriteLine("Hash: " + Utils.ToString(_work.Hash));
            Console.Write("Sending Share to Pool...");
            Console.ForegroundColor = ConsoleColor.White;
            if (_pool.SendShare(share))
                Console.Write("Share accepted!\n");
            else
                Console.Write("Server declined the Share!\n");
        }

        private static DateTime _lastPrint = DateTime.Now;
        private static void PrintCurrentState()
        {
            if (printcounter != 10)
            {
                printcounter+=1;
                return;
            }
            printcounter = 0;
            double progress = ((double)_nonce / uint.MaxValue) * 100;
            //
            TimeSpan span = DateTime.Now - _lastPrint;
            Console.WriteLine("Speed: " + (int)(((_batchSize) / 100) / span.TotalSeconds) + "Kh/sec " + "Share progress:" + progress.ToString("F2") + "%");
            _lastPrint = DateTime.Now;
        }

        private static void Print(string msg)
        {
            Console.WriteLine(msg);
            Console.WriteLine();
        }

        private static string ReadLineDefault(string defaultValue)
        {
            //Allow Console.ReadLine with a default value
            string userInput = Console.ReadLine();
            if (userInput == "")
                return defaultValue;
            else
                return userInput;
        }
    }

    class Utils
    {
        public static byte[] ToBytes(string input)
        {
            byte[] bytes = new byte[input.Length / 2];
            for (int i = 0, j = 0; i < input.Length; j++, i += 2)
                bytes[j] = byte.Parse(input.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);

            return bytes;
        }

        public static string ToString(byte[] input)
        {
            string result = "";
            foreach (byte b in input)
                result += b.ToString("x2");

            return result;
        }

        public static string ToString(uint value)
        {
            string result = "";
            foreach (byte b in BitConverter.GetBytes(value))
                result += b.ToString("x2");

            return result;
        }

        public static string EndianFlip32BitChunks(string input)
        {
            //32 bits = 4*4 bytes = 4*4*2 chars
            string result = "";
            for (int i = 0; i < input.Length; i += 8)
                for (int j = 0; j < 8; j += 2)
                {
                    //append byte (2 chars)
                    result += input[i - j + 6];
                    result += input[i - j + 7];
                }
            return result;
        }

        public static string RemovePadding(string input)
        {
            //payload length: final 64 bits in big-endian - 0x0000000000000280 = 640 bits = 80 bytes = 160 chars
            return input.Substring(0, 160);
        }

        public static string AddPadding(string input)
        {
            //add the padding to the payload. It never changes.
            return input + "000000800000000000000000000000000000000000000000000000000000000000000000000000000000000080020000";
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ExtractGPSFromAVISubtitle
{
    class Program
    {
        const string subtitlePattern = @"(\w\S+):(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2}).+G\s+([-\d\.]+)\s([NS])\s+([-\d\.]+)\s([EW])\s+(\d+)km";
        static void Main(string[] args)
        {
            long gap = 60; // デフォルトで1時間空いていたら別ファイルに出力する
            string[] inputFileList = null;
            string specifiedOutput = "";
            string outputFile = "";
            int outputArgIdx = int.MinValue;
            int gapArgIdx = int.MinValue;
            var dataArrayList = new Dictionary<long, List<string>>();
            Match m;
            if (args.Length == 0)
            {
                Console.Error.WriteLine("Error: AVI file does not exist.");
                Environment.Exit(1);
            }
            else
            {
                int numOfValidFiles = 0;
                outputArgIdx = int.MinValue;
                // AVIファイルのリストをListではなく配列に格納したかったので、まずAVIファイルの総数を確認する
                for (int i = 0; i < args.Length; i++)
                {
                    if (i == outputArgIdx + 1)
                    {
                        m = Regex.Match(args[i], @".+\.gpx$");
                        if (m.Success)
                        {
                            if (!Path.IsPathRooted(args[i]))
                            {
                                // -o 相対パス の場合はカレントディレクトリの文字列をつなげる
                                specifiedOutput = args[i];
                                outputFile = Directory.GetCurrentDirectory() + "\\" + specifiedOutput;
                            }
                            else
                            {
                                // -o 絶対パス の場合はそのまま
                                specifiedOutput = args[i];
                                outputFile = specifiedOutput;
                            }
                            continue;
                        }
                        else if (Directory.Exists(args[i]))
                        {
                            // -o フォルダへのパス の場合はそのまま
                            specifiedOutput = args[i];
                            outputFile = specifiedOutput;
                        }
                        else
                        {
                            outputArgIdx = int.MinValue;
                        }
                    }
                    if (args[i].Equals("-o"))
                    {
                        outputArgIdx = i;
                        continue;
                    }
                    if (i == gapArgIdx + 1)
                    {
                        // -g GPS出力データを区切る間隔[分]
                        if (long.TryParse(args[i], out gap))
                        {
                            continue;
                        }
                        else
                        {
                            gapArgIdx = int.MinValue;
                        }
                    }
                    if (args[i].Equals("-g"))
                    {
                        gapArgIdx = i;
                        continue;
                    }
                    if (File.Exists(args[i]))
                    {
                        m = Regex.Match(args[i], @".+\.AVI$");
                        if (m.Success)
                        {
                            // 指定されたファイルの内、実在する拡張子がAVIのファイルをカウントする
                            numOfValidFiles++;
                        }
                    }
                    if (Directory.Exists(args[i]))
                    {
                        // 指定された実在するディレクトリ直下に存在するファイルの内、拡張子がAVIのファイルをカウントする
                        numOfValidFiles += (Directory.GetFiles(args[i], "*.AVI", SearchOption.TopDirectoryOnly).Length);
                    }
                }
                if (numOfValidFiles == 0)
                {
                    Console.Error.WriteLine("Error: AVI file does not exist.");
                    Environment.Exit(1);
                }
                inputFileList = new string[numOfValidFiles];
                int idx = 0;
                for (int i = 0; i < args.Length; i++)
                {
                    if (File.Exists(args[i]))
                    {
                        m = Regex.Match(args[i], @".+\.AVI$");
                        if (m.Success)
                        {
                            inputFileList[idx] = args[i];
                            idx++;
                        }
                    }
                    if (Directory.Exists(args[i]))
                    {
                        string[] names = Directory.GetFiles(args[i], "*.AVI", SearchOption.TopDirectoryOnly);
                        Array.Copy(names, 0, inputFileList, idx, names.Length);
                        idx += names.Length;
                    }
                }
            }
            for (int i=0; i< inputFileList.Length; i++)
            {
                string inputFile = inputFileList[i];
                List<string> subtitleList = null;
                Console.Out.WriteLine(i+1 + "/" + inputFileList.Length + " files");

                // バイナリモードでファイルを読み込む
                try
                {
                    using (BinaryReader reader = new BinaryReader(new FileStream(inputFile, FileMode.Open)))
                    {
                        string riffOrlist = "";
                        if (isList(reader, ref riffOrlist) && riffOrlist.Equals("RIFF"))
                        {
                            getDataSize(reader);
                            getFourCC(reader);
                            if (isList(reader, ref riffOrlist) && riffOrlist.Equals("LIST"))
                            {
                                int headerSize = getDataSize(reader);
                                getFourCC(reader);
                                reader.BaseStream.Seek(headerSize - 4, SeekOrigin.Current);
                                if (!isList(reader, ref riffOrlist))
                                {
                                    // Junk CHUNKがある場合
                                    int junkChunkSize = getDataSize(reader);
                                    reader.BaseStream.Seek(junkChunkSize - 0, SeekOrigin.Current);
                                    if (isList(reader, ref riffOrlist) && riffOrlist.Equals("LIST"))
                                    {
                                        // Data LISTから字幕文字列を取り出す
                                        subtitleList = pickUpSubtitle(reader);
                                    }
                                    else
                                    {
                                        Console.Error.WriteLine("Error: Data LIST does not exist.");
                                        Environment.Exit(1);
                                    }
                                }
                                else if (riffOrlist.Equals("LIST"))
                                {
                                    // Junk CHUNKがない場合(ない場合はないのでは？)
                                    // Data LISTから字幕文字列を取り出す
                                    subtitleList = pickUpSubtitle(reader);
                                }
                                else
                                {
                                    Console.Error.WriteLine("Error: Data LIST does not exist.");
                                    Environment.Exit(1);
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine("Error: Header LIST does not exist.");
                                Environment.Exit(1);
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: RIFF LIST does not exist.");
                            Environment.Exit(1);
                        }
                    }
                }
                catch(Exception e)
                {
                    Console.Error.WriteLine("Error: " + e.Message);
                    Environment.Exit(1);
                }
                if (subtitleList != null)
                {
                    for (int j=0; j<subtitleList.Count; j++)
                    {
                        string subtitle = subtitleList[j];
                        m = Regex.Match(subtitle, subtitlePattern);
                        if (!m.Success)
                        {
                            // GPSデータが存在しないなら、その字幕文字列を削除する
                            subtitleList.RemoveAt(j);
                            j--;
                        }
                    }

                    if (subtitleList.Count > 0)
                    {
                        // Listの先頭にAVIファイル名を挿入する
                        // (タイムスタンプが最も小さいAVIファイルを元に、出力するGPXファイル名を作成するため)
                        subtitleList.Insert(0, inputFile);

                        // タイムスタンプ順にAVIファイルをソートするため、
                        // タイムスタンプを100ns単位の整数値にしたものをキーにして字幕文字列のリストをDictionaryに格納する
                        string firstSubtitle = subtitleList[1];
                        m = Regex.Match(firstSubtitle, subtitlePattern);
                        if (m.Success)
                        {
                            string year = m.Groups[2].Value;
                            string month = m.Groups[3].Value;
                            string day = m.Groups[4].Value;
                            string hour = m.Groups[5].Value;
                            string minute = m.Groups[6].Value;
                            string second = m.Groups[7].Value;
                            DateTimeOffset jstTime = new DateTimeOffset(int.Parse(year), int.Parse(month), int.Parse(day), int.Parse(hour), int.Parse(minute), int.Parse(second), new TimeSpan(9, 0, 0));
                            long ticks = jstTime.UtcTicks;
                            dataArrayList.Add(ticks, subtitleList);
                        }
                        else
                        {
                            // GPSデータが存在しない字幕文字列は削除しているので、ここに来ることはない
                        }
                    }
                }
            }

            if (dataArrayList.Count == 0)
            {
                Console.Error.WriteLine("Error: GPS data does not exist.");
                Environment.Exit(1);
            }

            while (dataArrayList.Count != 0)
            {

                // タイムスタンプ順にAVIファイルをソートする
                List<long> keys = dataArrayList.Keys.ToList();
                keys.Sort();

                // タイムスタンプが最も小さいAVIファイルを元に、出力するGPXファイル名を作成する
                if (outputArgIdx == int.MinValue)
                {
                    string firstInputFile = (dataArrayList[keys[0]])[0];
                    m = Regex.Match(firstInputFile, @"(.+)\.[^\.]+$");
                    if (m.Success)
                    {
                        outputFile = m.Groups[1].Value + ".gpx";
                    }
                    else
                    {
                        Console.Error.WriteLine("Error: Output file name is invalid.");
                        Environment.Exit(1);
                    }
                }
                else
                {
                    if (Directory.Exists(specifiedOutput))
                    {
                        string firstInputFile = (dataArrayList[keys[0]])[0];
                        m = Regex.Match(firstInputFile, @".+\\([^\\]+)\.[^\.]+$");
                        if (m.Success)
                        {
                            outputFile = specifiedOutput + "\\" + m.Groups[1].Value + ".gpx";
                        }
                        else
                        {
                            Console.Error.WriteLine("Error: Output file name is invalid.");
                            Environment.Exit(1);
                        }
                    }
                    else if (File.Exists(outputFile))
                    {
                        m = Regex.Match(outputFile, @"(.+)\((\d+)\)\.gpx$");
                        if (m.Success)
                        {
                            outputFile = m.Groups[1].Value + "(" + (int.Parse(m.Groups[2].Value) + 1).ToString() + ")" + ".gpx";
                        }
                        else
                        {
                            m = Regex.Match(outputFile, @"(.+)\.gpx$");
                            if (m.Success)
                            {
                                outputFile = m.Groups[1].Value + "(2)" + ".gpx";
                            }
                            else
                            {
                                Console.Error.WriteLine("Error: File name extension does not exist.");
                                Environment.Exit(1);
                            }
                        }
                    }
                }

                // GPXファイルを作成する
                long lasttick = long.MinValue;
                long currenttick = 0;
                try
                {
                    using (var writer = new StreamWriter(outputFile))
                    {
                        bool firstOfAllFlag = true;
                        foreach (long key in keys)
                        {
                            List<string> subtitles = dataArrayList[key];
                            bool firstOfFilelFlag = true;
                            foreach (string subtitle in subtitles)
                            {
                                m = Regex.Match(subtitle, subtitlePattern);
                                if (m.Success)
                                {
                                    string product = m.Groups[1].Value;
                                    string year = m.Groups[2].Value;
                                    string month = m.Groups[3].Value;
                                    string day = m.Groups[4].Value;
                                    string hour = m.Groups[5].Value;
                                    string minute = m.Groups[6].Value;
                                    string second = m.Groups[7].Value;
                                    string latitude = m.Groups[8].Value;
                                    if (m.Groups[9].Value.Equals("S")) latitude = "-" + latitude;
                                    string longitude = m.Groups[10].Value;
                                    if (m.Groups[11].Value.Equals("W")) longitude = "-" + longitude;
                                    string velocity = m.Groups[12].Value;

                                    if (firstOfAllFlag)
                                    {
                                        writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                                        writer.WriteLine("<gpx version=\"1.1\" xmlns=\"http://www.topografix.com/GPX/1/1\" creator=\"ExtractGPSFromAVISubtitle\">");
                                        writer.WriteLine("<trk>");
                                        writer.Write("<name>" + product);
                                        writer.Write(String.Format("_{0}-{1:00}-{2:00}_{3:00}:{4:00}:{5:00}", year, month, day, hour, minute, second));
                                        writer.WriteLine("</name>");
                                        writer.WriteLine("<number>1</number>");
                                        writer.WriteLine("<trkseg>");
                                    }

                                    // 同じタイムスタンプのデータは出力させない
                                    DateTimeOffset jstTime = new DateTimeOffset(int.Parse(year), int.Parse(month), int.Parse(day), int.Parse(hour), int.Parse(minute), int.Parse(second), new TimeSpan(9, 0, 0));
                                    currenttick = jstTime.Ticks;
                                    if (firstOfFilelFlag && !firstOfAllFlag)
                                    {
                                        if (lasttick + gap*60 / 100e-9 <= currenttick) break;
                                    }
                                    if (lasttick >= currenttick) continue;

                                    // 緯度経度データを出力する
                                    DateTimeOffset utcTime = jstTime.ToUniversalTime(); // JSTをUTCに変換する
                                    string date_iso = String.Format("{0}-{1:00}-{2:00}T{3:00}:{4:00}:{5:00}Z", utcTime.Year, utcTime.Month, utcTime.Day, utcTime.Hour, utcTime.Minute, utcTime.Second);
                                    writer.WriteLine("<trkpt lat=\"" + latitude + "\" lon=\"" + longitude + "\">");
                                    writer.WriteLine("  <time>" + date_iso + "</time>");
                                    writer.WriteLine("</trkpt>");

                                    lasttick = currenttick;
                                    firstOfAllFlag = false;
                                    firstOfFilelFlag = false;
                                }
                            }
                            if (firstOfFilelFlag && !firstOfAllFlag)
                            {
                                if (lasttick + gap * 60 / 100e-9 <= currenttick) break;
                            }
                            dataArrayList.Remove(key);
                        }

                        if (!firstOfAllFlag)
                        {
                            writer.WriteLine("</trkseg>");
                            writer.WriteLine("</trk>");
                            writer.WriteLine("</gpx>");
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("Error: " + e.Message);
                    Environment.Exit(1);
                }
            }
        }

        static List<string> pickUpSubtitle(BinaryReader reader)
        {
            List<string> subtitleList = new List<string>();
            int moviListSize = getDataSize(reader);
            int dataListSizeCounter = 0;
            if (getFourCC(reader).Equals("movi"))
            {
                dataListSizeCounter += 4;
                string chunk = "";
                while (dataListSizeCounter < moviListSize)
                {
                    int dataChunkSize;
                    if (!isList(reader, ref chunk) && chunk.Equals("02tx"))
                    {
                        // 字幕用Data CHUNKなら、そのデータを字幕文字列としてListに格納する
                        dataChunkSize = getDataSize(reader);
                        string subtitle = getString(reader, dataChunkSize);
                        subtitleList.Add(subtitle);
                    }
                    else
                    {
                        // 字幕用Data CHUNK以外なら、そのデータを読みにいかずスキップする
                        dataChunkSize = getDataSize(reader);
                        reader.BaseStream.Seek(dataChunkSize - 0, SeekOrigin.Current);
                    }
                    dataListSizeCounter += (8 + dataChunkSize);
                }
            }
            else
            {
                Console.Error.WriteLine("Error: Data LIST does not exist.");
                Environment.Exit(1);
            }
            return subtitleList;
        }

        static Boolean isList(BinaryReader reader, ref string name)
        {
            name = getFourCC(reader);
            return name.Equals("RIFF") || name.Equals("LIST");
        }

        static int getDataSize(BinaryReader reader)
        {
            int size = reader.ReadInt32();
            if (size % 2 != 0) size++; // サイズが奇数なら偶数にする
            return size;
        }
        static string getString(BinaryReader reader, int size)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(size));
        }
        static string getFourCC(BinaryReader reader)
        {
            return getString(reader, 4);
        }
    }
}

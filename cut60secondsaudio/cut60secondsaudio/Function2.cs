using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Linq;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Text;
using System.IO.Compression;
using Microsoft.Azure.WebJobs.Host;

namespace cut60secondsaudio
{
    public static class Function2
    {
        [FunctionName("Function2")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
             [Blob("cutaudio", Connection = "StorageConnectionString")] CloudBlobContainer cutaudio,
            TraceWriter log)
        {


            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            AudioChunk chunks = new AudioChunk();
            AudioInfo audio = new AudioInfo();


            JsonSerializer serializer = new JsonSerializer();
            var textString = JsonConvert.DeserializeObject<Class1>(requestBody);

            string recordingUrl = textString.recordingurl;
            string resultSubString = recordingUrl.Substring(0, recordingUrl.IndexOf("?sv"));
            string[] result2 = resultSubString.Split("/");
            string audioName = result2[4];

            foreach (Segmentresult result in textString.SegmentResults)
            {
                if ((result.OffsetInSeconds + result.DurationInSeconds) - chunks.start < 60)
                {
                    chunks.Text += result.NBest.FirstOrDefault().Display;
                    chunks.stop = result.OffsetInSeconds + result.DurationInSeconds;
                }
                else
                {
                    audio.AudioChunks.Add(chunks);
                    float stop = chunks.stop;
                    chunks = new AudioChunk();
                    chunks.start = stop;
                    chunks.Text += result.NBest.FirstOrDefault().Display;
                }
            }
            audio.AudioChunks.Add(chunks);



            int count = 0;

            var guid = Guid.NewGuid();
            var rootDirectory = Path.GetTempPath();
            var tempPath = Path.Combine(rootDirectory, guid.ToString());

            log.Info($"Temo path: {rootDirectory}");


            Directory.CreateDirectory(tempPath);

            var tempText = tempPath + "\\adioTextFile.txt";

            CloudBlobClient cbc = new CloudBlobClient(new Uri(recordingUrl));
            var thing = await cbc.GetBlobReferenceFromServerAsync(new Uri(recordingUrl));
            string localFileName = Path.Combine(tempPath, audioName);
            await thing.DownloadToFileAsync(localFileName, FileMode.CreateNew);

            try
            {
                using (FileStream fs = File.Create(tempText))
                {
                    foreach (AudioChunk audioElements in audio.AudioChunks)
                    {
                        using (var process = new Process())
                        {
                            process.StartInfo.FileName = @"D:\home\site\wwwroot\ffmpeg.exe";

                            // Getting start and stop times for the audio cuts //

                            TimeSpan time = TimeSpan.FromSeconds(audioElements.start);
                            string timeStart = time.ToString(@"hh\:mm\:ss");
                            TimeSpan duration = TimeSpan.FromSeconds(audioElements.stop) - time;
                            string TimeStop = duration.ToString(@"hh\:mm\:ss");

                            // Declare the name/location of the audio  //

                            string chunkAudioName = "chunk" + count.ToString() + "_" + audioName;
                            var tempOut = tempPath + "\\" + chunkAudioName;

                            // Cut the audio using ffmpeg


                            log.Info($"TempOut: {tempOut}");

                            process.StartInfo.Arguments = @"-i " + localFileName + " -ss " + timeStart + " -t " + TimeStop + " -async 1 " + tempOut;
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.RedirectStandardOutput = true;
                            process.StartInfo.RedirectStandardError = true;
                            process.Start();
                            string output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds);

                            // Add text to file    

                           log.Info($"Output: {process.ExitCode}");
                       //  log.Error(process.StandardError.ReadToEnd());
                            log.Info(process.StandardOutput.ReadToEnd());

                            Byte[] text = new UTF8Encoding(true).GetBytes(chunkAudioName + "\t" + audioElements.Text + "\n");
                            fs.Write(text, 0, text.Length);
                            count++;
                        }
                    }
                }

                var zipFileOut = (rootDirectory + guid + ".zip");
                ZipFile.CreateFromDirectory(tempPath, zipFileOut);
                var zipFile = guid + ".zip";
                var cloudBlockBlob = cutaudio.GetBlockBlobReference(zipFile);
                await cloudBlockBlob.UploadFromFileAsync(zipFileOut);
            }
            finally
            {
                Directory.Delete(tempPath, true);
                File.Delete(rootDirectory + guid + ".zip");

            }

            return audioName != null
                ? (ActionResult)new OkObjectResult("200")
                : new BadRequestObjectResult("Bad Request");
        }
    }
}

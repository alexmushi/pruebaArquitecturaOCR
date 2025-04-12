using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Adobe.PDFServicesSDK;
using Adobe.PDFServicesSDK.auth;
using Adobe.PDFServicesSDK.exception;
using Adobe.PDFServicesSDK.io;
using Adobe.PDFServicesSDK.pdfjobs.jobs;
using Adobe.PDFServicesSDK.pdfjobs.results;
using log4net;
using log4net.Config;
using log4net.Repository;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DotNetEnv;

namespace OcrPDF
{
    class Program
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(Program));

        static void Main()
        {
            ConfigureLogging();

            string outputFilePath = "";

            try
            {
                // Leer credenciales desde el archivo .env
                Env.Load();
                var clientId = Environment.GetEnvironmentVariable("CLIENT_ID");
                var clientSecret = Environment.GetEnvironmentVariable("CLIENT_SECRET");

                // Validar que no estén vacías
                if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                    throw new Exception("Las variables de entorno CLIENT_ID o CLIENT_SECRET no están definidas.");

                // Crear instancia de credenciales
                ICredentials credentials = new ServicePrincipalCredentials(clientId, clientSecret);

                PDFServices pdfServices = new PDFServices(credentials);

                // Subir archivo de entrada
                using Stream inputStream = File.OpenRead("ocrInput.pdf");
                IAsset asset = pdfServices.Upload(inputStream, PDFServicesMediaType.PDF.GetMIMETypeValue());

                // Crear y enviar el trabajo de OCR
                OCRJob ocrJob = new OCRJob(asset);
                string location = pdfServices.Submit(ocrJob);
                PDFServicesResponse<OCRResult> pdfServicesResponse = pdfServices.GetJobResult<OCRResult>(location, typeof(OCRResult));

                // Obtener el contenido del resultado
                IAsset resultAsset = pdfServicesResponse.Result.Asset;
                StreamAsset streamAsset = pdfServices.GetContent(resultAsset);

                // Guardar el archivo PDF resultante
                outputFilePath = CreateOutputFilePath();
                new FileInfo(Directory.GetCurrentDirectory() + outputFilePath).Directory.Create();
                using Stream outputStream = File.OpenWrite(Directory.GetCurrentDirectory() + outputFilePath);
                streamAsset.Stream.CopyTo(outputStream);
            }
            catch (ServiceUsageException ex) { log.Error("ServiceUsageException", ex); }
            catch (ServiceApiException ex) { log.Error("ServiceApiException", ex); }
            catch (SDKException ex) { log.Error("SDKException", ex); }
            catch (IOException ex) { log.Error("IOException", ex); }
            catch (Exception ex) { log.Error("General Exception", ex); }

            // Si el archivo se generó exitosamente, revisa si contiene "INE"
            if (!string.IsNullOrEmpty(outputFilePath) && File.Exists(Directory.GetCurrentDirectory() + outputFilePath))
            {
                string fullPath = Directory.GetCurrentDirectory() + outputFilePath;
                string text = ExtractTextFromPdf(fullPath);

                if (text.Contains("ELECTORAL", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Parece ser un INE");
                }
                else
                {
                    Console.WriteLine("No se detectó un INE");
                }
            }
        }

        static void ConfigureLogging()
        {
            ILoggerRepository logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));
        }

        private static string CreateOutputFilePath()
        {
            string timeStamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss");
            return $"/output/ocr_{timeStamp}.pdf";
        }

        private static string ExtractTextFromPdf(string filePath)
        {
            using var document = PdfDocument.Open(filePath);
            var text = "";
            foreach (Page page in document.GetPages())
            {
                text += page.Text;
            }
            return text;
        }
    }
}

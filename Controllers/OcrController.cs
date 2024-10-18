using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SkiaSharp;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Tesseract;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfiumViewer;

namespace NewTessTry.Controllers
{
    public class OcrController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpPost("upload-image")]
        public async Task<IActionResult> UploadImage(IFormFile file, string outputFormat)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", file.FileName);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var text = ExtractTextFromImage(filePath);

                if (outputFormat.ToLower() == "json")
                {
                    return Json(new { text });
                }
                else if (outputFormat.ToLower() == "xml")
                {
                    return Content($"<text>{text}</text>", "application/xml");
                }
                else
                {
                    return BadRequest("Invalid output format.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
            finally
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
        }

        [HttpPost("upload-pdf")]
        public async Task<IActionResult> UploadPdf(IFormFile file, string outputFormat)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Uploads", file.FileName);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var text = ExtractTextFromPdf(filePath);

                if (outputFormat.ToLower() == "json")
                {
                    return Json(new { text });
                }
                else if (outputFormat.ToLower() == "xml")
                {
                    return Content($"<text>{text}</text>", "application/xml");
                }
                else
                {
                    return BadRequest("Invalid output format.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
            finally
            {
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
        }

        private string ExtractTextFromPdf(string filePath)
        {
            using (var pdfReader = new PdfReader(filePath))
            using (var pdfDocument = new iText.Kernel.Pdf.PdfDocument(pdfReader))
            {
                var text = new StringWriter();
                for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
                {
                    var page = pdfDocument.GetPage(i);
                    var strategy = new SimpleTextExtractionStrategy();
                    var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                    if (string.IsNullOrWhiteSpace(pageText))
                    {
                        // If no text is extracted, try OCR
                        var ocrText = ExtractTextFromPdfPageUsingOcr(page, i);
                        text.WriteLine(ocrText);
                    }
                    else
                    {
                        text.WriteLine(pageText);
                    }
                }
                return text.ToString();
            }
        }

        private static string ExtractTextFromPdfPageUsingOcr(iText.Kernel.Pdf.PdfPage pdfPage, int pageNumber)
        {
            // Convert PDF page to image using PdfiumViewer
            using var pdfStream = new MemoryStream();
            var pdfWriter = new iText.Kernel.Pdf.PdfWriter(pdfStream);
            var pdfDocument = new iText.Kernel.Pdf.PdfDocument(pdfWriter);
            pdfDocument.AddPage(pdfPage.CopyTo(pdfDocument));
            pdfDocument.Close();
            pdfStream.Seek(0, SeekOrigin.Begin);

            using var document = PdfiumViewer.PdfDocument.Load(pdfStream);
            using var image = document.Render(pageNumber - 1, 300, 300, true);
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Seek(0, SeekOrigin.Begin);

            // Use Tesseract to extract text from image
            using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
            using var img = Pix.LoadFromMemory(stream.ToArray());
            using var page = engine.Process(img);
            return page.GetText();
        }

        private string ExtractTextFromImage(string filePath)
        {
            try
            {
                using (var input = SKBitmap.Decode(filePath))
                {
                    if (input == null)
                    {
                        throw new Exception("Failed to decode image.");
                    }

                    using (var grayImage = new SKBitmap(input.Width, input.Height, SKColorType.Gray8, SKAlphaType.Opaque))
                    {
                        using (var canvas = new SKCanvas(grayImage))
                        {
                            canvas.DrawBitmap(input, 0, 0, new SKPaint
                            {
                                ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                                {
                                            0.299f, 0.587f, 0.114f, 0, 0,
                                            0.299f, 0.587f, 0.114f, 0, 0,
                                            0.299f, 0.587f, 0.114f, 0, 0,
                                            0, 0, 0, 1, 0
                                })
                            });
                        }

                        using (var image = SKImage.FromBitmap(grayImage))
                        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                        using (var stream = new MemoryStream())
                        {
                            data.SaveTo(stream);
                            stream.Seek(0, SeekOrigin.Begin);

                            using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
                            {
                                using (var img = Pix.LoadFromMemory(stream.ToArray()))
                                {
                                    using (var page = engine.Process(img))
                                    {
                                        return page.GetText();
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to extract text from image: {ex.Message}", ex);
            }
        }
    }
}

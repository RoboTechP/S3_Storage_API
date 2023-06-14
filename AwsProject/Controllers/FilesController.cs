using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using WebApplication1.Model;

namespace AwsProject.Controllers
{
    [Route("api/files")]
    [ApiController]
    public class FilesController : ControllerBase
    {
        private readonly IAmazonS3 _s3Client;
        public FilesController(IAmazonS3 s3Client)
        {
            _s3Client = s3Client;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFileAsync(IFormFile file, string bucketName, string? prefix)
        {
            var bucketExists = await _s3Client.DoesS3BucketExistAsync(bucketName);
            if (!bucketExists) return NotFound($"Bucket {bucketName} does not exist.");

            using (var stream = file.OpenReadStream())
            {
                var config = new TransferUtilityConfig();
                config.ConcurrentServiceRequests = 10;
                config.MinSizeBeforePartUpload = 16 * 1024 * 1024;
                config.NumberOfUploadThreads = 10;

                var transferUtility = new TransferUtility(_s3Client, config);

                var uploadRequest = new TransferUtilityUploadRequest
                {
                    BucketName = bucketName,
                    Key = string.IsNullOrEmpty(prefix) ? file.FileName : $"{prefix?.TrimEnd('/')}/{file.FileName}",
                    InputStream = stream,
                    ContentType = file.ContentType
                };

              
            

                await transferUtility.UploadAsync(uploadRequest);
            }

            return Ok($"File {prefix}/{file.FileName} uploaded to S3 via multipart successfully!");
        }
      

        [HttpGet("download-file")]
        public async Task<IActionResult> DownloadFile(string bucketName, string? prefix)
        {
            var bucketExists = await _s3Client.DoesS3BucketExistAsync(bucketName);
            if (!bucketExists)
                return NotFound($"Bucket {bucketName} does not exist.");

            var request = new ListObjectsV2Request()
            {
                BucketName = bucketName,
                Prefix = prefix
            };

            var result = await _s3Client.ListObjectsV2Async(request);
            var s3Objects = result.S3Objects;

            // Choose the first object for simplicity
            var s3Object = s3Objects.FirstOrDefault();
            if (s3Object == null)
                return NotFound("No objects found in the specified bucket and prefix.");

            var downloadRequest = new TransferUtilityDownloadRequest
            {
                BucketName = bucketName,
                Key = s3Object.Key,
                FilePath = "C:\\Users\\User\\Desktop\\" + prefix
            };

            downloadRequest.WriteObjectProgressEvent += OnDownloadProgress;

            using (var transferUtility = new TransferUtility(_s3Client))
            {
                await transferUtility.DownloadAsync(downloadRequest);
            }

            return Ok("File downloaded successfully.");
        }

        private void OnDownloadProgress(object sender, WriteObjectProgressArgs e)
        {
            // You can handle the download progress here if needed
            long tranfered = e.TransferredBytes / 1000000;
            long total = e.TotalBytes / 1000000;

            Console.WriteLine($"Downloaded {tranfered}/{total} MB");
        }

        [HttpGet("get-all")]
        public async Task<IActionResult> GetAllFilesAsync(string bucketName, string? prefix)
        {
            var bucketExists = await _s3Client.DoesS3BucketExistAsync(bucketName);
            if (!bucketExists) return NotFound($"Bucket {bucketName} does not exist.");
            var request = new ListObjectsV2Request()
            {
                BucketName = bucketName,
                Prefix = prefix
            };
            var result = await _s3Client.ListObjectsV2Async(request);
            var s3Objects = result.S3Objects.Select(s =>
            {
                var urlRequest = new GetPreSignedUrlRequest()
                {
                    BucketName = bucketName,
                    Key = s.Key,
                    Expires = DateTime.UtcNow.AddMinutes(1)
                };
                return new S3ObjectDto()
                {
                    Name = s.Key.ToString(),
                    PresignedUrl = _s3Client.GetPreSignedURL(urlRequest),
                };
            });
            return Ok(s3Objects);
        }

        [HttpGet("get-by-key")]
        public async Task<IActionResult> GetFileByKeyAsync(string bucketName, string key)
        {
            var bucketExists = await _s3Client.DoesS3BucketExistAsync(bucketName);
            if (!bucketExists) return NotFound($"Bucket {bucketName} does not exist.");
            var s3Object = await _s3Client.GetObjectAsync(bucketName, key);
            return File(s3Object.ResponseStream, s3Object.Headers.ContentType);
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFileAsync(string bucketName, string key)
        {
            var bucketExists = await _s3Client.DoesS3BucketExistAsync(bucketName);
            if (!bucketExists) return NotFound($"Bucket {bucketName} does not exist");
            await _s3Client.DeleteObjectAsync(bucketName, key);
            return NoContent();
        }

    }
}


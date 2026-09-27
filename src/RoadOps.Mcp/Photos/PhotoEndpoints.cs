using Microsoft.AspNetCore.Mvc;
using RoadOps.Application.Common;
using RoadOps.Application.Services;
using RoadOps.Mcp.Auth;

namespace RoadOps.Mcp.Photos;

/// <summary>
/// Plain HTTP endpoints for photo bytes, next to the MCP endpoint. Images never pass through the agent: the field app
/// uploads the file here, gets a photo ID back, and the agent only passes that ID to the attach_photo tool.
/// </summary>
public static class PhotoEndpoints
{
    /// <summary>Request body cap: the photo limit plus room for multipart framing.</summary>
    private const long MaxRequestBytes = PhotoService.MaxPhotoBytes + 64 * 1024;

    public static RouteGroupBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty);

        // Accepts multipart/form-data (field "file") or a raw image body. The declared content type and file name are
        // ignored: the server detects the type from the bytes and names the stored file itself.
        group.MapPost("/uploads/photos", async (HttpContext http, PhotoService photos, CancellationToken cancellationToken) =>
            {
                var caller = Caller.NameOf(http.User);
                try
                {
                    UploadedPhotoResponse response;
                    if (http.Request.HasFormContentType)
                    {
                        var form = await http.Request.ReadFormAsync(cancellationToken);
                        var file = form.Files.GetFile("file");
                        if (file is null || form.Files.Count != 1)
                        {
                            return Results.BadRequest(new { error = "Send exactly one file in the form field \"file\"." });
                        }

                        await using var stream = file.OpenReadStream();
                        response = UploadedPhotoResponse.From(await photos.UploadAsync(stream, caller, cancellationToken));
                    }
                    else
                    {
                        response = UploadedPhotoResponse.From(await photos.UploadAsync(http.Request.Body, caller, cancellationToken));
                    }

                    return Results.Created($"/photos/{response.PhotoId}", response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty) });
                }
            })
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .DisableAntiforgery(); // API-key/bearer auth, no cookies, so no CSRF exposure.

        group.MapGet("/photos/{photoId}", async (string photoId, HttpContext http, PhotoService photos, CancellationToken cancellationToken) =>
        {
            var found = await photos.OpenAsync(photoId, cancellationToken);
            if (found is not { } photo)
            {
                return Results.NotFound();
            }

            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.Stream(photo.Content, photo.Photo.ContentType);
        });

        return group;
    }

    public sealed record UploadedPhotoResponse(string PhotoId, string ContentType, long SizeBytes, string Sha256)
    {
        public static UploadedPhotoResponse From(RoadOps.Application.Field.UploadedPhoto photo) =>
            new(photo.Id, photo.ContentType, photo.SizeBytes, photo.Sha256);
    }
}

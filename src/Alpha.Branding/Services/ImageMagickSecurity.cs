using ImageMagick;
using ImageMagick.Configuration;
using System.IO;

namespace Alpha.Branding.Services;

public static class ImageMagickSecurity
{
    // Covers 200 MP phone photos (16320x12240); larger images are rejected before their pixels are allocated.
    public const uint MaxImageDimension = 20000;

    // The last matching rule wins. A module must have read and write rights or ImageMagick will not register it,
    // so denied formats are also missing from MagickNET.SupportedFormats and MagickFormatInfo throws.
    // XC backs the solid-colour canvases the app creates; MNG and JNG are part of the PNG module.
    private const string Policy = """
        <policymap>
          <policy domain="delegate" rights="none" pattern="*" />
          <policy domain="module" rights="none" pattern="*" />
          <policy domain="module" rights="read | write" pattern="{BMP,HEIC,JPEG,PNG,WEBP,XC}" />
          <policy domain="coder" rights="none" pattern="{MNG,JNG}" />
          <policy domain="path" rights="none" pattern="@*" />
          <policy domain="path" rights="none" pattern="|*" />
        </policymap>
        """;

    // ImageMagick reads its policy once, on first use, so this must run before any other ImageMagick call.
    public static void Apply()
    {
        var files = ConfigurationFiles.Default;
        files.Policy.Data = Policy;

        var directory = Directory.CreateTempSubdirectory("Alpha_Branding_ImageMagick_");
        try
        {
            MagickNET.Initialize(files, directory.FullName);
            EnsurePolicyIsActive();
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
                // The policy is already loaded; a leftover temp folder is harmless.
            }
        }

        ResourceLimits.Width = MaxImageDimension;
        ResourceLimits.Height = MaxImageDimension;
    }

    private static void EnsurePolicyIsActive()
    {
        try
        {
            using var svg = new MagickImage("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1\" height=\"1\"/>"u8.ToArray());
        }
        catch (MagickPolicyErrorException)
        {
            return;
        }

        throw new InvalidOperationException(
            "The ImageMagick security policy did not take effect. It must be applied before any other ImageMagick call.");
    }
}

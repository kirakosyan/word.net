using System;
using System.IO;
using System.Linq;
using WordNet.Internal;
using WordNet.Model;

namespace WordNet.Formats;

internal static partial class DocFormat
{
    // MS-DOC PICF/PICMID and MS-ODRAW inline shape/BLIP records.
    private static DocumentImage ReadPicture(byte[] data, int offset, ConversionContext context)
    {
        Bounds(data, offset, 68); int length = I32(data, offset); Bounds(data, offset, length);
        if (length < 68 || U16(data, offset + 4) != 68) throw new InvalidDataException("Doc.Picture: Invalid picture header.");
        int type = U16(data, offset + 6);
        if (type is not 100 and not 102) throw new NotSupportedException("Doc.Picture: Only OfficeArt inline pictures are supported.");
        double width = unchecked((short)U16(data, offset + 28)) * U16(data, offset + 32) / 20000.0;
        double height = unchecked((short)U16(data, offset + 30)) * U16(data, offset + 34) / 20000.0;
        if (Slice(data, offset + 46, 16).Any(b => b != 0)) context.Loss("Doc.ImageBorder", "Picture borders are not retained.");
        int begin = offset + 68;
        if (type == 102) { Bounds(data, begin, 1); begin += 1 + data[begin]; }
        byte[]? image = null;
        Scan(begin, offset + length, 0);
        if (image == null) throw new NotSupportedException("Doc.Picture: Inline picture does not contain a supported PNG or JPEG BLIP.");
        return DocumentImage.Read(image, width, height, "", context);

        void Scan(int start, int end, int depth)
        {
            if (depth > context.Options.MaximumNestingDepth) throw new InvalidDataException("Limit.NestingDepth: OfficeArt nesting exceeds the limit.");
            while (start < end)
            {
                context.CheckCancellation();
                if (end - start < 8) throw new InvalidDataException("Doc.Picture: Truncated OfficeArt record.");
                int options = U16(data, start), recordType = U16(data, start + 2), size = I32(data, start + 4), payload = start + 8;
                if (size < 0 || size > end - payload) throw new InvalidDataException("Doc.Picture: Invalid OfficeArt size.");
                if (recordType == 0xf004) Scan(payload, payload + size, depth + 1);
                else if (recordType == 0xf007)
                {
                    if (size < 36 || 36 + data[payload + 33] > size) throw new InvalidDataException("Doc.Picture: Invalid BLIP store entry.");
                    Scan(payload + 36 + data[payload + 33], payload + size, depth + 1);
                }
                else if (recordType is 0xf01e or 0xf01d or 0xf02a)
                {
                    int instance = options >> 4;
                    bool png = recordType == 0xf01e;
                    bool two = instance == (png ? 0x6e1 : 0x46b) || !png && instance == 0x6e3;
                    if ((options & 15) != 0 || !(png ? instance is 0x6e0 or 0x6e1 : instance is 0x46a or 0x46b or 0x6e2 or 0x6e3))
                        throw new InvalidDataException("Doc.Picture: Invalid BLIP header.");
                    int skip = two ? 33 : 17;
                    if (size <= skip) throw new InvalidDataException("Doc.Picture: Empty BLIP payload.");
                    if (image != null) context.Loss("Doc.ImageVariants", "Only the first embedded raster variant is retained.");
                    else image = Slice(data, payload + skip, size - skip);
                }
                else if (recordType is 0xf00b or 0xf121 or 0xf122)
                {
                    int count = options >> 4;
                    if (count > size / 6) throw new InvalidDataException("Doc.Picture: Truncated shape properties.");
                    for (int i = 0; i < count; i++)
                        if ((U16(data, payload + i * 6) & 0x3fff) != 0x104)
                            context.Loss("Doc.ImageEffects", "Additional picture shape properties and effects are not retained.");
                }
                else if (recordType == 0xf00a)
                {
                    if (size != 8) throw new InvalidDataException("Doc.Picture: Invalid shape record.");
                    if ((I32(data, payload + 4) & 0xc0) != 0) context.Loss("Doc.ImageEffects", "Mirrored picture transforms are not retained.");
                }
                else context.Loss("Doc.PictureRecord", "Unsupported picture shape records are not retained.");
                start = payload + size;
            }
        }
    }

    private static byte[] WritePicture(DocumentImage image, int id, ConversionContext context)
    {
        if (Math.Round(image.Width * 20) < 1 || Math.Round(image.Height * 20) < 1)
            throw new NotSupportedException("Doc.ImageSize: Image dimensions are too small for binary Word twip precision.");
        if (image.AlternativeText.Length != 0) context.Loss("Doc.ImageAlternativeText", "This binary Word profile does not retain image alternative text.");
        byte[] shape = new byte[8]; W32(shape, 0, id); W32(shape, 4, 0x800);
        byte[] property = new byte[6]; W16(property, 0, 0x4104); W32(property, 2, 1);
        byte[] container = Art(0x000f, 0xf004, Join(Art((75 << 4) | 2, 0xf00a, shape), Art(0x13, 0xf00b, property)));
        byte[] uid = ImageIdentifier.Compute(image.Bytes);
        byte[] blip = Art(image.Extension == "png" ? 0x6e00 : 0x46a0, image.Extension == "png" ? 0xf01e : 0xf01d, Join(uid, new byte[] { 255 }, image.Bytes));
        // Inline rgfb permits bare BLIPs as well as FBSE records. Use an explicit store
        // entry so readers can also inspect the image type, identity and reference count.
        int type = image.Extension == "png" ? 6 : 5;
        byte[] entry = new byte[36]; entry[0] = entry[1] = (byte)type;
        Array.Copy(uid, 0, entry, 2, uid.Length); W16(entry, 18, 255);
        W32(entry, 20, blip.Length); W32(entry, 24, 1);
        byte[] store = Art((type << 4) | 2, 0xf007, Join(entry, blip));
        byte[] header = new byte[68]; W32(header, 0, checked(68 + container.Length + store.Length)); W16(header, 4, 68); W16(header, 6, 100);
        W16(header, 28, checked((int)Math.Round(image.Width * 20))); W16(header, 30, checked((int)Math.Round(image.Height * 20)));
        W16(header, 32, 1000); W16(header, 34, 1000);
        return Join(header, container, store);
    }

    private static byte[] Art(int options, int type, byte[] payload)
    {
        byte[] header = new byte[8]; W16(header, 0, options); W16(header, 2, type); W32(header, 4, payload.Length); return Join(header, payload);
    }
}

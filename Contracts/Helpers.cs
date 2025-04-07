namespace Contracts;

public static class FloatBase64Helper
{
    /// <summary>
    /// Converts a list of floats to a Base64-encoded string.
    /// </summary>
    public static string ConvertFloatListToBase64(List<float> floatList)
    {
        // Allocate enough bytes for all floats (4 bytes each).
        byte[] bytes = new byte[4 * floatList.Count];

        // Copy the float data into the byte array.
        // Buffer.BlockCopy can copy from float[] to byte[] directly.
        Buffer.BlockCopy(floatList.ToArray(), 0, bytes, 0, bytes.Length);

        // Convert the raw bytes to Base64 text.
        return Convert.ToBase64String(bytes);
    }


    /// <summary>
    /// Converts a Base64-encoded string back into a list of floats (float32).
    /// </summary>
    public static List<float> ConvertBase64ToFloatList(string base64)
    {
        // Decode from Base64 to raw bytes
        byte[] bytes = Convert.FromBase64String(base64);

        // Each float is 4 bytes
        float[] floatArray = new float[bytes.Length / 4];

        // Copy from the byte[] into the float array
        Buffer.BlockCopy(bytes, 0, floatArray, 0, bytes.Length);

        return new List<float>(floatArray);
    }
}

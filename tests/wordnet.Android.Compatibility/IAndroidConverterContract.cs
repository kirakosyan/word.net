namespace WordNet.Tests.Android;

// Compile-only check that a .NET for Android consumer can reference the portable API.
// This does not execute a converter or validate native assets on an Android device.
internal interface IAndroidConverterContract : IDocumentConverter
{
}

using System.Resources;
using System.Windows;

// The compiled XAML is in US English, so the ResourceManager that loads it goes straight to this assembly instead
// of first looking for satellite assemblies of the UI culture.
[assembly: NeutralResourcesLanguage("en-US")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

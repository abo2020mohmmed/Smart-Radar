using System.Xml.Linq;

namespace WinImageStudio.Core;

public static class UnattendWriter
{
    public static XDocument Create(UnattendOptions options)
    {
        Validation.CheckUnattend(options);
        XNamespace ns = "urn:schemas-microsoft-com:unattend";
        XElement Component(string name, params object[] content) => new(ns + "component",
            new XAttribute("name", name), new XAttribute("processorArchitecture", options.Architecture),
            new XAttribute("publicKeyToken", "31bf3856ad364e35"), new XAttribute("language", "neutral"),
            new XAttribute("versionScope", "nonSxS"), content);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(ns + "unattend",
            new XElement(ns + "settings", new XAttribute("pass", "specialize"),
                Component("Microsoft-Windows-Shell-Setup", new XElement(ns + "ComputerName", options.ComputerName), new XElement(ns + "TimeZone", options.TimeZone))),
            new XElement(ns + "settings", new XAttribute("pass", "oobeSystem"),
                Component("Microsoft-Windows-International-Core", new XElement(ns + "InputLocale", options.InputLocale),
                    new XElement(ns + "SystemLocale", options.Language), new XElement(ns + "UILanguage", options.Language), new XElement(ns + "UserLocale", options.Language)))));
    }
}

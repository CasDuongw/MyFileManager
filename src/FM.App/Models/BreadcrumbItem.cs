namespace FM.App.Models;

public record BreadcrumbItem(string Name, string FullPath, bool IsLast);

public record BreadcrumbFolderItem(string Name, string FullPath);

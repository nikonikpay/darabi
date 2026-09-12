namespace Mazesta.Hardware.Wmi;
public interface IWmiQuery { IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql); }

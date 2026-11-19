using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;

namespace Editors.Ipc
{
    // The pipe's generic way into any editor: an editor's view model read by path, the way its own bindings read it.
    // A path is property names and [ ] steps: "Tags[3].Variables[FieldName=StartTime].ValueAsString", where [3] is an item
    // of a list, [Name=x] the first item whose Name is x, and [key] a dictionary entry. Set writes a property as a two-way
    // binding would, and Invoke runs a command or a public method as a button would, so the editor reacts as it does to its
    // own controls. It knows no editor, which is why it reaches every one; the price is the editor's internal names.
    public static partial class ViewModelPaths
    {
        // Kind: value, null, object, list, command or method. Value is a value's text in the form Set takes back.
        public record Member(string Path, string Kind, string Type, string? Value = null, bool? Writable = null, int? Count = null, bool? CanExecute = null);

        // Items listed from one list in a tree; past it the list's Count still says how many there are.
        public const int ListItems = 100;

        [GeneratedRegex(@"\G(?:\.?(?<name>[A-Za-z_][A-Za-z0-9_]*)|\[(?<index>[^\]]*)\])")]
        private static partial Regex Segment();

        public static List<(string? Name, string? Index)> Parse(string? path)
        {
            var segments = new List<(string?, string?)>();
            path = path?.Trim() ?? "";
            for (var at = 0; at < path.Length;)
            {
                var match = Segment().Match(path, at);
                if (!match.Success || match.Length == 0)
                    throw new ArgumentException($"Can't read the path from \"{path[at..]}\"");
                segments.Add(match.Groups["name"].Success ? (match.Groups["name"].Value, null) : (null, match.Groups["index"].Value));
                at += match.Length;
            }
            return segments;
        }

        public static object? Resolve(object root, string? path)
        {
            object? current = root;
            var at = "";
            foreach (var segment in Parse(path))
            {
                current = Step(current, segment, at);
                at += segment.Name != null ? (at.Length == 0 ? segment.Name : "." + segment.Name) : $"[{segment.Index}]";
            }
            return current;
        }

        static object? Step(object? current, (string? Name, string? Index) segment, string at)
        {
            if (current == null)
                throw new ArgumentException($"{(at.Length == 0 ? "The editor" : at)} is null");
            if (segment.Name != null)
            {
                if (Property(current.GetType(), segment.Name) is { } property)
                    return property.GetValue(current);
                // NotifyAttr<T> is a value in a box: a step it has no property for goes on into its Value.
                if (IsNotifyAttr(current.GetType()))
                    return Step(Unwrap(current), segment, at);
                throw new ArgumentException($"{Describe(current.GetType())}{(at.Length == 0 ? "" : $" at {at}")} has no property {segment.Name}");
            }

            var index = segment.Index!.Trim();
            if (IsNotifyAttr(current.GetType()))
                current = Unwrap(current) ?? throw new ArgumentException($"{at} is null");
            if (current is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    if (string.Equals(Format(entry.Key), index, StringComparison.OrdinalIgnoreCase))
                        return entry.Value;
                throw new ArgumentException($"{at} has no key {index}");
            }
            if (int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                if (current is IList list)
                    return i >= 0 && i < list.Count ? list[i] : throw new ArgumentException($"{at} has {list.Count} items; [{i}] is past them");
                if (current is IEnumerable items and not string)
                    return items.Cast<object?>().Skip(i).Take(1).ToList() is [var item] && i >= 0 ? item : throw new ArgumentException($"{at} has no item [{i}]");
            }
            var equals = index.IndexOf('=');
            if (equals > 0 && current is IEnumerable rows and not string)
            {
                var (name, wanted) = (index[..equals].Trim(), index[(equals + 1)..].Trim());
                foreach (var row in rows)
                    if (row != null && Property(row.GetType(), name) is { } p && string.Equals(Format(p.GetValue(row)), wanted, StringComparison.OrdinalIgnoreCase))
                        return row;
                throw new ArgumentException($"No item of {at} has {name} = {wanted}");
            }
            throw new ArgumentException($"{at} ({Describe(current.GetType())}) can't take [{index}]");
        }

        // The most derived property of that name: a "new" property hides its base's, as it does for a binding.
        static PropertyInfo? Property(Type type, string name)
        {
            foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
                for (var t = type; t != null; t = t.BaseType)
                    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        if (p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true && string.Equals(p.Name, name, comparison))
                            return p;
            return null;
        }

        public static (List<Member> Members, bool Truncated) Tree(object root, string? path, int depth, int limit)
        {
            var start = Resolve(root, path);
            var prefix = path?.Trim() ?? "";
            var members = new List<Member>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var truncated = false;
            Walk(start, prefix, Math.Max(depth, 1));
            return (members, truncated);

            void Add(Member member)
            {
                if (members.Count >= limit)
                    truncated = true;
                else
                    members.Add(member);
            }

            void Walk(object? obj, string at, int depthLeft)
            {
                if (obj == null || truncated || !seen.Add(obj))
                    return;
                if (obj is IEnumerable items and not string)
                {
                    var i = 0;
                    foreach (var item in items)
                    {
                        if (i == ListItems || truncated)
                            break;
                        var itemPath = $"{at}[{i++}]";
                        Add(Describe(item, item?.GetType() ?? typeof(object), itemPath, null));
                        if (depthLeft > 1 && Opens(item))
                            Walk(item, itemPath, depthLeft - 1);
                    }
                    return;
                }
                foreach (var property in Properties(obj.GetType()))
                {
                    var memberPath = at.Length == 0 ? property.Name : $"{at}.{property.Name}";
                    object? value;
                    try { value = property.GetValue(obj); }
                    catch (Exception e) { Add(new Member(memberPath, "error", Describe(property.PropertyType), (e.InnerException ?? e).Message)); continue; }
                    Add(Describe(value, property.PropertyType, memberPath, Writable(property, value)));
                    var inner = IsNotifyAttr(property.PropertyType) ? Unwrap(value) : value;
                    if (depthLeft > 1 && Opens(inner))
                        Walk(inner, memberPath, depthLeft - 1);
                }
                foreach (var method in Methods(obj.GetType()))
                {
                    var parameter = method.GetParameters().SingleOrDefault();
                    Add(new Member(at.Length == 0 ? method.Name : $"{at}.{method.Name}", "method", parameter == null ? "" : Describe(parameter.ParameterType)));
                }
            }
        }

        public static Member Get(object root, string path)
        {
            var (parent, last) = Parent(root, path);
            if (last.Name != null && parent != null && Property(parent.GetType(), last.Name) is { } property)
                return Describe(property.GetValue(parent), property.PropertyType, path.Trim(), Writable(property, property.GetValue(parent)));
            var value = Resolve(root, path);
            return Describe(value, value?.GetType() ?? typeof(object), path.Trim(), null);
        }

        public static Member Set(object root, string path, string? text)
        {
            var (parent, last) = Parent(root, path);
            if (parent == null)
                throw new ArgumentException($"The owner of {path} is null");
            if (last.Name != null)
            {
                var property = Property(parent.GetType(), last.Name);
                if (property == null && IsNotifyAttr(parent.GetType()))
                    (parent, property) = (Unwrap(parent)!, Property(Unwrap(parent)!.GetType(), last.Name));
                if (property == null)
                    throw new ArgumentException($"{Describe(parent.GetType())} has no property {last.Name}");
                var current = property.GetValue(parent);
                if (IsNotifyAttr(property.PropertyType) && current != null)
                {
                    var valueProperty = current.GetType().GetProperty(nameof(NotifyAttr<int>.Value))!;
                    valueProperty.SetValue(current, Convert(root, text, valueProperty.PropertyType));
                }
                else if (property.SetMethod?.IsPublic == true)
                    property.SetValue(parent, Convert(root, text, property.PropertyType));
                else
                    throw new ArgumentException($"{path} can't be set: it has no public setter");
                return Get(root, path);
            }
            if (parent is IList list && int.TryParse(last.Index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            {
                if (list.IsReadOnly || list.IsFixedSize && parent is not Array)
                    throw new ArgumentException($"{path} is in a list that can't be changed");
                list[i] = Convert(root, text, ElementType(parent.GetType()));
                return Get(root, path);
            }
            throw new ArgumentException($"{path} can't be set");
        }

        // A command's parameter, or a method's one argument, is text converted to what it takes, or "@path" for an object.
        public static Member Invoke(object root, string path, string? text)
        {
            var (owner, last) = Parent(root, path);
            if (owner == null || last.Name == null)
                throw new ArgumentException($"{path} is not a command or a method");
            if (Property(owner.GetType(), last.Name) is { } property && property.GetValue(owner) is ICommand command)
            {
                var parameter = text == null ? null : text.StartsWith('@') ? Convert(root, text, typeof(object)) : text;
                if (!command.CanExecute(parameter))
                    throw new ArgumentException($"{path} can't run now (CanExecute is false)");
                command.Execute(parameter);
                return new Member(path.Trim(), "command", Describe(command.GetType()), CanExecute: command.CanExecute(parameter));
            }
            var methods = owner.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, last.Name, StringComparison.OrdinalIgnoreCase) && !m.IsGenericMethodDefinition && m.GetParameters().Length == (text == null ? 0 : 1))
                .ToList();
            if (methods.Count == 0)
                throw new ArgumentException($"{Describe(owner.GetType())} has no command or public method {last.Name} taking {(text == null ? "nothing" : "one value")}");
            var method = methods[0];
            object?[] arguments = text == null ? [] : [Convert(root, text, method.GetParameters()[0].ParameterType)];
            var result = method.Invoke(owner, arguments);
            if (result is Task)
                return new Member(path.Trim(), "method", "Task", "started");
            return method.ReturnType == typeof(void)
                ? new Member(path.Trim(), "method", "void")
                : Describe(result, method.ReturnType, path.Trim(), null);
        }

        static (object? Parent, (string? Name, string? Index) Last) Parent(object root, string path)
        {
            var segments = Parse(path);
            if (segments.Count == 0)
                throw new ArgumentException("No path given");
            object? parent = root;
            var at = "";
            foreach (var segment in segments.Take(segments.Count - 1))
            {
                parent = Step(parent, segment, at);
                at += segment.Name != null ? (at.Length == 0 ? segment.Name : "." + segment.Name) : $"[{segment.Index}]";
            }
            return (parent, segments[^1]);
        }

        public static object? Convert(object root, string? text, Type type)
        {
            if (text != null && text.StartsWith("@@"))
                text = text[1..];
            else if (text != null && text.StartsWith('@'))
            {
                var found = Resolve(root, text[1..]);
                return found == null || type.IsInstanceOfType(found) ? found
                    : throw new ArgumentException($"{text[1..]} is {Describe(found.GetType())}, not {Describe(type)}");
            }
            var underlying = Nullable.GetUnderlyingType(type);
            if (text == null || (text == "null" && (underlying != null || !type.IsValueType) && type != typeof(string)))
                return !type.IsValueType || underlying != null ? null : throw new ArgumentException($"{Describe(type)} can't be null");
            var t = underlying ?? type;
            if (t == typeof(object) || t == typeof(string))
                return text;
            if (t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4) || t == typeof(Quaternion))
            {
                var n = text.Split(',').Select(x => float.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray();
                return (t.Name, n.Length) switch
                {
                    (nameof(Vector2), 2) => new Vector2(n[0], n[1]),
                    (nameof(Vector3), 3) => new Vector3(n[0], n[1], n[2]),
                    (nameof(Vector4), 4) => new Vector4(n[0], n[1], n[2], n[3]),
                    (nameof(Quaternion), 4) => new Quaternion(n[0], n[1], n[2], n[3]),
                    _ => throw new ArgumentException($"{Describe(t)} takes {(t == typeof(Vector2) ? 2 : t == typeof(Vector3) ? 3 : 4)} numbers, \"x,y,...\""),
                };
            }
            if (t.IsEnum)
                return Enum.Parse(t, text, ignoreCase: true);
            var converter = TypeDescriptor.GetConverter(t);
            if (converter.CanConvertFrom(typeof(string)))
                return converter.ConvertFromInvariantString(text);
            throw new ArgumentException($"Text can't become {Describe(t)}; pass \"@path\" to an object of that type");
        }

        static Member Describe(object? value, Type declared, string path, bool? writable)
        {
            if (IsNotifyAttr(declared) || value != null && IsNotifyAttr(value.GetType()))
            {
                var inner = Unwrap(value);
                var valueType = (value?.GetType() ?? declared).GetGenericArguments()[0];
                return Describe(inner, valueType, path, value != null);
            }
            if (value == null)
                return new Member(path, "null", Describe(declared), Writable: writable);
            var type = value.GetType();
            if (value is ICommand command)
                return new Member(path, "command", Describe(type), CanExecute: SafeCanExecute(command));
            if (IsValue(type))
                return new Member(path, "value", Describe(type), Format(value), writable);
            if (value is IEnumerable)
                return new Member(path, "list", Describe(type), Writable: writable, Count: (value as ICollection)?.Count);
            return new Member(path, "object", Describe(type), Writable: writable);
        }

        static bool SafeCanExecute(ICommand command)
        {
            try { return command.CanExecute(null); }
            catch { return false; }
        }

        static bool? Writable(PropertyInfo property, object? value) =>
            IsNotifyAttr(property.PropertyType) ? value != null : property.SetMethod?.IsPublic == true;

        static bool IsValue(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) ||
            type == typeof(TimeSpan) || type == typeof(Guid) || type.IsValueType && type.Namespace == "Microsoft.Xna.Framework";

        // Only the app's own objects and lists are walked into; a framework object (a WPF control, a GraphicsDevice) is
        // listed by its type and left closed, since its members are the framework's, not the editor's.
        static bool Opens(object? value) =>
            value != null && !IsValue(value.GetType()) && value is not ICommand && (value is IEnumerable || IsAppType(value.GetType()));

        static bool IsAppType(Type type) =>
            type.Namespace is { } ns && !(ns.StartsWith("System") || ns.StartsWith("Microsoft") || ns.StartsWith("MS.") ||
                                          ns.StartsWith("CommunityToolkit") || ns.StartsWith("Serilog") || ns.StartsWith("MonoGame"));

        static IEnumerable<PropertyInfo> Properties(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod?.IsPublic == true && IsAppType(p.DeclaringType!)
                            && !typeof(Delegate).IsAssignableFrom(p.PropertyType))
                .GroupBy(p => p.Name).Select(g => g.OrderByDescending(p => Depth(p.DeclaringType!)).First())
                .OrderBy(p => Depth(p.DeclaringType!)).ThenBy(p => p.MetadataToken);

        // Public methods of the app's own types taking at most one argument: what a code-behind click handler calls.
        static IEnumerable<MethodInfo> Methods(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition && m.GetParameters().Length <= 1 && IsAppType(m.DeclaringType!))
                .GroupBy(m => m.Name).Select(g => g.First())
                .OrderBy(m => m.Name, StringComparer.Ordinal);

        static int Depth(Type type)
        {
            var depth = 0;
            for (var t = type.BaseType; t != null; t = t.BaseType)
                depth++;
            return depth;
        }

        static Type ElementType(Type listType) =>
            listType.IsArray ? listType.GetElementType()!
            : listType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>))?.GetGenericArguments()[0] ?? typeof(object);

        static bool IsNotifyAttr(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(NotifyAttr<>);

        static object? Unwrap(object? value) => value != null && IsNotifyAttr(value.GetType()) ? value.GetType().GetProperty(nameof(NotifyAttr<int>.Value))!.GetValue(value) : value;

        // Values in the form Convert reads back: invariant numbers, and vectors as "x,y,z".
        public static string? Format(object? value) => value switch
        {
            null => null,
            Vector2 v => Invariant($"{v.X},{v.Y}"),
            Vector3 v => Invariant($"{v.X},{v.Y},{v.Z}"),
            Vector4 v => Invariant($"{v.X},{v.Y},{v.Z},{v.W}"),
            Quaternion q => Invariant($"{q.X},{q.Y},{q.Z},{q.W}"),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };

        static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

        static string Describe(Type type) =>
            type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>" : type.Name;
    }
}

# CyreneMetadata

一个编译期元数据库，通过源代码生成器收集派生类型、属性和构造函数信息，替代运行时反射。

[![NuGet](https://img.shields.io/nuget/v/CyreneMetadata.svg)](https://www.nuget.org/packages/CyreneMetadata/)
[![License](https://img.shields.io/github/license/CyreneLaboratory/CyreneMetadata)](https://github.com/CyreneLaboratory/CyreneMetadata/blob/main/LICENSE.txt)

## 特性

- **零反射** - 派生类型、属性、构造函数在编译期收集并生成静态注册代码，运行时直接查表
- **按需生成** - 只有代码里实际调用过的类型才会被收集，不产生多余元数据
- **AOT 兼容** - 完全支持 Native AOT 与裁剪，不触发 trimmer 警告
- **自动注册** - 通过 `[ModuleInitializer]` 在程序启动时自动完成注册，无需手动初始化

## 安装

```bash
dotnet add package CyreneMetadata
```

## 快速开始

直接调用 `CyreneMetadataCache` 的查询方法，生成器会识别调用中 `typeof(...)` 的根类型并在编译期收集其元数据。参数必须是编译期已知的字面 `typeof`，不能传运行时 `Type` 变量。

### 派生类型

传入基类或接口，获取程序集内所有可访问的派生类型：

```csharp
using CyreneMetadata;

Type[] shapes = CyreneMetadataCache.GetDerive(typeof(Shape));

foreach (var type in shapes)
{
    var shape = (Shape)CyreneMetadataCache.CreateInstance(type); // 编译期生成的无参构造工厂
}
```

- 只收集有 `public` 无参构造函数的类型才可用于 `CreateInstance`
- 支持未绑定泛型基类（如 `typeof(Repository<>)`）

### 属性元数据

传入类型，获取其属性列表（含基类属性），并通过委托读写：

```csharp
using CyreneMetadata;

CyrenePropertyInfo[] props = CyreneMetadataCache.GetProperties(typeof(User));

var user = new User();
foreach (var prop in props)
{
    if (prop.CanWrite && prop.HasAttribute(typeof(RequiredAttribute)))
        prop.Set!(user, defaultValue);

    object? value = prop.CanRead ? prop.Get!(user) : null;
}
```

`GetProperties` 支持 `BindingFlags` 重载，语义与反射一致（`Public`/`NonPublic`/`Instance`/`Static`/`DeclaredOnly`/`FlattenHierarchy`）。

| 成员 | 说明 |
| --- | --- |
| `Name` / `PropertyType` / `DeclaringType` | 属性名、类型、声明类型 |
| `IsStatic` / `IsPublic` | 修饰符信息 |
| `Get` / `Set` | 读写委托，不可读写时为 `null` |
| `CanRead` / `CanWrite` | 是否可读写 |
| `Attributes` / `HasAttribute(Type)` | 属性上标注的特性类型 |

## 要求

- .NET 8.0 或更高版本
- C# 12 或更高版本

## 许可证

本项目采用 [MIT](LICENSE.txt)。

## 链接

- [NuGet](https://www.nuget.org/packages/CyreneMetadata/)
- [GitHub](https://github.com/CyreneLaboratory/CyreneMetadata)
- [作者](https://github.com/Letheriver2007)

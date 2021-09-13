The CFG configuration format is a text format for configuration files which is similar to, and a superset of, the JSON format. It dates from before its first announcement in [2008](https://wiki.python.org/moin/HierConfig) and has the following aims:

* Allow a hierarchical configuration scheme with support for key-value mappings and lists.
* Support cross-references between one part of the configuration and another.
* Provide a string interpolation facility to easily build up configuration values from other configuration values.
* Provide the ability to compose configurations (using include and merge facilities).
* Provide the ability to access real application objects safely, where supported by the platform.
* Be completely declarative.

It overcomes a number of drawbacks of JSON when used as a configuration format:

* JSON is more verbose than necessary.
* JSON doesn’t allow comments.
* JSON doesn’t provide first-class support for dates and multi-line strings.
* JSON doesn’t allow trailing commas in lists and mappings.
* JSON doesn’t provide easy cross-referencing, interpolation, or composition.

Installation
============
The library can be installed using `nuget` and the package name `RedDove.Config`.

Exploration
============
To explore CFG functionality for .NET, we use the `dotnet-script` Read-Eval-Print-Loop (REPL), which is available from [here](https://github.com/filipw/dotnet-script). Once installed, you can invoke a shell using
```
$ dotnet dotnet-script
```

Getting Started with CFG in C#
==============================
A configuration is represented by an instance of the `Config` struct. The constructor for this class can be passed a filename or a stream which contains the text for the configuration. The text is read in, parsed and converted to an object that you can then query. A simple example:

```
a: 'Hello, '
b: 'world!'
c: {
  d: 'e'
}
'f.g': 'h'
christmas_morning: `2019-12-25 08:39:49`
home: `$HOME`
foo: `$FOO|bar`
```

Loading a configuration
=======================
The configuration above can be loaded as shown below. In the REPL shell:

```
> #r "RedDove.Config.dll"
> using RedDove.Config;
> var cfg = new Config("test0a.cfg");
> cfg["a"]
"Hello, "
> cfg["b"]
"world!"
```

Access elements with keys
=========================
Accessing elements of the configuration with a simple key is just like using a `Dictionary<string, object>`:

```
> cfg["a"]
"Hello, "
> cfg["b"]
"world!"
```
You can see the types and values of the returned objects are as expected.

Access elements with paths
==========================
As well as simple keys, elements  can also be accessed using `path` strings:
```
> cfg["c.d"]
"e"
```
Here, the desired value is obtained in a single step, by (under the hood) walking the path `c.d` – first getting the mapping at key `c`, and then the value at `d` in the resulting mapping.

Note that you can have simple keys which look like paths:
```
> cfg["f.g"]
"h"
```
If a key is given that exists in the configuration, it is used as such, and if it is not present in the configuration, an attempt is made to interpret it as a path. Thus, `f.g` is present and accessed via key, whereas `c.d` is not an existing key, so is interpreted as a path.

Access to date/time objects
===========================
You can also get native CLR `System.DateTime` and `System.DateTimeOffset` objects from a configuration, by using an ISO date/time pattern in a `backtick-string`:
```
> cfg["christmas_morning"]
[25/12/2019 08:39:49]
```

Access to other CLR objects
===========================
Access to other CLR objects is also possible using the backtick-string syntax, provided that they are either environment values or objects accessible via public static fields, properties or methods which take no arguments:
```
> cfg["access"]
ReadWrite
> cfg["today"]
[15/01/2020 00:00:00]
```

Access to environment variables
===============================

To access an environment variable, use a `backtick-string` of the form `$VARNAME`:
```
> cfg["home"].Equals(Environment.GetEnvironmentVariable("HOME"))
true
```
You can specify a default value to be used if an environment variable isn’t present using the `$VARNAME|default-value` form. Whatever string follows the pipe character (including the empty string) is returned if `VARNAME` is not a variable in the environment.
```
> cfg["foo"]
"bar"
```
For more information, see [the CFG documentation](https://docs.red-dove.com/cfg/index.html).

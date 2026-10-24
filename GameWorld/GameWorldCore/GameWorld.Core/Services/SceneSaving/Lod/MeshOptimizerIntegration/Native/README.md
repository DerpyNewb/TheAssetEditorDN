# meshoptimizer.dll

meshoptimizer 1.3 by Arseny Kapoulkine, MIT licence (`LICENSE.md` here). Source: https://github.com/zeux/meshoptimizer/releases/tag/v1.3

Built from `meshoptimizer-1.3.tar.gz` (sha256 `ca4f47d451201593904b9e3067fa54b65db94bc34ab5173864950862b748a145`) with MSVC 14.44, x64, static CRT, so it needs no Visual C++ redistributable:

```
cmake -S meshoptimizer-1.3 -B build -G "Visual Studio 17 2022" -A x64 -DMESHOPT_BUILD_SHARED_LIBS=ON -DMESHOPT_INSTALL=OFF -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded
cmake --build build --config Release
```

Copy `build/Release/meshoptimizer.dll` here. `GameWorld.Core.csproj` copies it to the output folder.

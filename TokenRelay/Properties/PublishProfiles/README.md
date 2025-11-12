# TokenRelay Publish Profiles

This directory contains publish profiles for different deployment scenarios of TokenRelay.

## Available Profiles

### 1. StandaloneOptimized.pubxml (Windows x64)
**Recommended for Windows production deployment**

- **Target**: Windows x64
- **Self-Contained**: Yes (includes .NET runtime)
- **Single File**: Yes (all dependencies in one executable)
- **ReadyToRun**: Yes (faster startup)
- **Deployment Size**: ~100MB
- **Requirements**: None (fully standalone)

```powershell
# Publish using this profile
dotnet publish -p:PublishProfile=StandaloneOptimized

# Or using MSBuild
dotnet msbuild -p:PublishProfile=StandaloneOptimized -p:DeployOnBuild=true
```

### 2. LinuxOptimized.pubxml (Linux x64)
**Recommended for Linux production deployment**

- **Target**: Linux x64
- **Self-Contained**: Yes (includes .NET runtime)
- **Single File**: Yes (all dependencies in one executable)
- **ReadyToRun**: Yes (faster startup)
- **Deployment Size**: ~100MB
- **Requirements**: None (fully standalone)

```bash
# Publish using this profile
dotnet publish -p:PublishProfile=LinuxOptimized
```

### 3. FrameworkDependent.pubxml (Cross-Platform)
**For environments with .NET runtime already installed**

- **Target**: Any platform
- **Self-Contained**: No (requires .NET runtime)
- **Single File**: Yes (application code only)
- **ReadyToRun**: Yes (faster startup)
- **Deployment Size**: ~10MB
- **Requirements**: .NET 8.0 Runtime

```powershell
# Publish using this profile
dotnet publish -p:PublishProfile=FrameworkDependent
```

## Profile Features

### PublishSingleFile=true
- Bundles all application dependencies into a single executable
- Simplifies deployment (just copy one file)
- Faster startup due to fewer file system operations
- Automatic extraction of native libraries when needed

### PublishReadyToRun=true
- Pre-compiles assemblies to native code
- Significantly faster application startup
- Reduced JIT compilation overhead
- Larger file size but better performance

### Additional Optimizations
- **IncludeNativeLibrariesForSelfExtract**: Bundles native dependencies
- **EnableCompressionInSingleFile**: Reduces file size through compression
- **InvariantGlobalization**: Reduces culture-specific overhead
- **UseSystemResourceKeys**: Optimizes resource usage

## Usage Examples

### Visual Studio
1. Right-click project → Publish
2. Select the desired profile
3. Click "Publish"

### Command Line - Quick Publish
```powershell
# Windows optimized
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true

# Linux optimized  
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true

# Framework dependent
dotnet publish -c Release --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

### Using the Standalone Publisher Script
The `publish-standalone.ps1` script automatically uses optimized settings similar to these profiles:

```powershell
# Uses ReadyToRun and SingleFile optimizations
.\publish-standalone.ps1 -Runtime win-x64 -SelfContained $true
```

## Performance Benefits

| Feature | Startup Time | File Size | JIT Overhead |
|---------|-------------|-----------|--------------|
| Standard | Baseline | Baseline | High |
| SingleFile | -20% | +10% | Medium |
| ReadyToRun | -40% | +30% | Low |
| Both | -50% | +35% | Minimal |

## Deployment Considerations

### Single File Deployment
- ✅ Simple deployment (one file)
- ✅ Faster startup
- ✅ Reduced attack surface
- ❌ Larger file size
- ❌ Temporary extraction on first run

### ReadyToRun Optimization
- ✅ Significantly faster startup
- ✅ Reduced CPU usage during startup
- ✅ Better cold start performance
- ❌ Larger file size
- ❌ Longer build times

## Troubleshooting

### Common Issues

1. **File too large**: Disable compression or use framework-dependent deployment
2. **Slow first startup**: Normal for single file - subsequent starts are faster
3. **Missing native dependencies**: Ensure `IncludeNativeLibrariesForSelfExtract=true`

### Profile Customization

You can create custom profiles by copying existing ones and modifying:
- `RuntimeIdentifier` for different platforms
- `PublishTrimmed=true` for smaller size (with potential compatibility issues)
- `PublishAot=true` for ultimate performance (requires .NET 8+)

## Platform Support

| Runtime ID | Platform | Architecture |
|------------|----------|--------------|
| win-x64 | Windows | 64-bit |
| win-x86 | Windows | 32-bit |
| linux-x64 | Linux | 64-bit |
| linux-arm64 | Linux | ARM 64-bit |
| osx-x64 | macOS | Intel 64-bit |
| osx-arm64 | macOS | Apple Silicon |

For a complete list: `dotnet --info`

@echo off
setlocal
cd /d "%~dp0"

set BB=lib\byte-buddy-1.18.17.jar
set OUT=dist

where javac >nul 2>nul
if errorlevel 1 (
  echo [x] PATH 里没有 javac。装 JDK 17 后重试。
  exit /b 1
)

if not exist "%BB%" (
  echo [x] 缺少 %BB%
  echo     下载地址：https://repo1.maven.org/maven2/net/bytebuddy/byte-buddy/1.18.17/byte-buddy-1.18.17.jar
  echo     下好后放进 lib\ 目录。
  exit /b 1
)

if exist build rmdir /s /q build
mkdir build\agent
mkdir build\attach
if not exist %OUT% mkdir %OUT%

echo [1/5] 编译代理（目标 Java 8，兼容 1.7 ~ 1.21 服务端）
javac --release 8 -encoding UTF-8 -nowarn -cp "%BB%" -d build\agent ^
  src\club\njitmcl\agent\Bridge.java ^
  src\club\njitmcl\agent\BridgeLoginAdvice.java ^
  src\club\njitmcl\agent\BridgeDomainAdvice.java ^
  src\club\njitmcl\agent\BridgeSkinAdvice.java ^
  src\club\njitmcl\agent\BridgeSignatureAdvice.java ^
  src\club\njitmcl\agent\BridgeValidateAdvice.java ^
  src\club\njitmcl\agent\BridgeSecureAdvice.java ^
  src\club\njitmcl\agent\AgentMain.java ^
  src\club\njitmcl\agent\AvatarTextureAdvice.java ^
  src\club\njitmcl\agent\TextureResolveProbe.java ^
  src\club\njitmcl\agent\PlayerEntryAdvice.java ^
  src\club\njitmcl\agent\PlayerEntryProbe.java ^
  src\club\njitmcl\agent\SkinOwnerProbe.java ^
  src\club\njitmcl\agent\TextureDomainGate.java ^
  src\club\njitmcl\agent\PeerSkin.java ^
  src\club\njitmcl\agent\PeerSkinAdvice.java ^
  src\club\njitmcl\agent\SkinCache.java ^
  src\club\njitmcl\agent\AuthFlagInlineAdvice.java ^
  src\club\njitmcl\agent\ValidatorInlineAdvice.java ^
  src\club\njitmcl\agent\AgentState.java ^
  src\club\njitmcl\agent\AuthAdvice.java ^
  src\club\njitmcl\agent\AuthFlagAdvice.java ^
  src\club\njitmcl\agent\HasSignatureAdvice.java ^
  src\club\njitmcl\agent\HostProfileAdvice.java ^
  src\club\njitmcl\agent\HostProfileSetAdvice.java ^
  src\club\njitmcl\agent\HostSkin.java ^
  src\club\njitmcl\agent\Json.java ^
  src\club\njitmcl\agent\Log.java ^
  src\club\njitmcl\agent\PackedTexturesAdvice.java ^
  src\club\njitmcl\agent\Profiles.java ^
  src\club\njitmcl\agent\PropertyAdvice.java ^
  src\club\njitmcl\agent\Sessions.java ^
  src\club\njitmcl\agent\TextureDomainAdvice.java ^
  src\club\njitmcl\agent\TraceAdvice.java ^
  src\club\njitmcl\agent\ValidatorTrueAdvice.java ^
  src\club\njitmcl\agent\ShapePatcher.java ^
  src\club\njitmcl\agent\SkinDebugAdvice.java ^
  src\club\njitmcl\agent\SkinDebugThrowAdvice.java ^
  src\club\njitmcl\agent\SkinFutureAdvice.java ^
  src\club\njitmcl\agent\SkinLookupAdvice.java ^
  src\club\njitmcl\agent\SkinProfileAdvice.java ^
  src\club\njitmcl\agent\SkinRenderAdvice.java ^
  src\club\njitmcl\agent\SkinStageAdvice.java ^
  src\club\njitmcl\agent\SkinSyncAdvice.java ^
  src\club\njitmcl\agent\SkinStageThrowAdvice.java ^
  src\club\njitmcl\agent\SkinSecureAdvice.java ^
  src\club\njitmcl\agent\SkinSecureFlagAdvice.java ^
  src\club\njitmcl\agent\SignatureValidatorAdvice.java ^
  src\club\njitmcl\agent\SingleplayerAdvice.java ^
  src\club\njitmcl\agent\ValidatePropertyAdvice.java ^
  src\club\njitmcl\agent\ControlServer.java
if errorlevel 1 goto fail

echo [2/5] 编译运行时挂载器
javac --release 8 -encoding UTF-8 -nowarn -d build\attach src\club\njitmcl\agent\Attacher.java
if errorlevel 1 goto fail

echo [3/5] 内联 ByteBuddy（免去额外 jar 与 Class-Path 配置）
pushd build\agent
jar xf "..\..\%BB%"
if exist META-INF\MANIFEST.MF del /q META-INF\MANIFEST.MF
if exist module-info.class del /q module-info.class
popd

echo [4/5] 打包
> build\agent.mf (
  echo Premain-Class: club.njitmcl.agent.AgentMain
  echo Agent-Class: club.njitmcl.agent.AgentMain
  echo Can-Redefine-Classes: true
  echo Can-Retransform-Classes: true
)
jar cfm %OUT%\njit-online-agent.jar build\agent.mf -C build\agent .
if errorlevel 1 goto fail
jar cfe %OUT%\njit-online-attach.jar club.njitmcl.agent.Attacher -C build\attach .
if errorlevel 1 goto fail

echo [5/5] 完成
for %%F in (%OUT%\*.jar) do echo     %%~nxF  %%~zF 字节
echo.
echo 把 dist 里的两个 jar 放到 PCL-NJITMCL.exe 同级目录即可。
exit /b 0

:fail
echo.
echo [x] 构建失败，查看上面的 javac 输出。
exit /b 1

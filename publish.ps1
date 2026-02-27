$rids = "win-x64","linux-x64","linux-arm64","osx-x64","osx-arm64"
foreach($rid in $rids) {
  dotnet publish -c Release -r $rid --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true -o "publish/$rid"
}
foreach ($item in Get-ChildItem publish) { zip $item.name "publish/$($item.name)" -r }
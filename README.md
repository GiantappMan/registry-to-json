# registry-to-json
Export the registry as a JSON file.

# How to use
```
# install 
dotnet tool install --global rtj

# upgarde
dotnet tool update --global rtj
```
```
# run in commandline
rtj -r '计算机\HKEY_CURRENT_USER\SOFTWARE\Cognosphere\Star Rail' -o star-rail.json
```

也支持直接监控某个字段（注册表值）：
```
rtj -r 'HKEY_CURRENT_USER\Software\miHoYo\原神:GENERAL_DATA_h2389025596' -o genshin-value.json
```

GUI 中的“注册表路径”输入框同样支持这种 `键路径:字段名` 格式。

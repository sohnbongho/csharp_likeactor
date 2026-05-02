protoc-34.1-win64\bin\protoc.exe -I=. --csharp_out=. message.proto

copy Message.cs  ..\Library\DTO\Message.cs
pause

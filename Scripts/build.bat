protoc -I=. --csharp_out=. message.proto

copy Message.cs  ..\Library\DTO\Message.cs
pause

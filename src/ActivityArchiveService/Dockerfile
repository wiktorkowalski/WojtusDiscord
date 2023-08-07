FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/ActivityArchiveService/ActivityArchiveService.csproj", "src/ActivityArchiveService/"]
RUN dotnet restore "src/ActivityArchiveService/ActivityArchiveService.csproj"
COPY . .
WORKDIR "/src/src/ActivityArchiveService"
RUN dotnet build "ActivityArchiveService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "ActivityArchiveService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ActivityArchiveService.dll"]

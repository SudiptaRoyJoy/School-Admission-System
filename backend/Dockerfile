# Build and publish the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Admission.Api/Admission.Api.csproj Admission.Api/
RUN dotnet restore Admission.Api/Admission.Api.csproj
COPY Admission.Api/ Admission.Api/
RUN dotnet publish Admission.Api/Admission.Api.csproj -c Release -o /app/publish --no-restore

# Small runtime image. The same image is used by both independent API processes.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "Admission.Api.dll"]

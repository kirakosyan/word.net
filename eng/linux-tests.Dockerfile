FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime8
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=runtime8 /usr/share/dotnet/shared/Microsoft.NETCore.App/ /usr/share/dotnet/shared/Microsoft.NETCore.App/
RUN apt-get update && apt-get install -y --no-install-recommends libtesseract5 libfontconfig1 fontconfig ca-certificates curl && rm -rf /var/lib/apt/lists/*
RUN mkdir /tessdata && curl -fsSL https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata -o /tessdata/eng.traineddata \
    && echo '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2  /tessdata/eng.traineddata' | sha256sum -c -
ENV WORDNET_TESSDATA=/tessdata
WORKDIR /work
COPY . .
RUN mkdir -p /usr/local/share/fonts/wordnet && cp src/wordnet/Resources/Fonts/*.ttf /usr/local/share/fonts/wordnet/ && fc-cache -f
# Optional build-only workaround for QEMU's executable-memory aliasing. Native builds retain W^X.
ARG DOTNET_EnableWriteXorExecute=1
RUN dotnet restore wordnet.sln --locked-mode && dotnet build wordnet.sln -c Release --no-restore
ENTRYPOINT ["bash", "eng/run-linux-tests.sh"]

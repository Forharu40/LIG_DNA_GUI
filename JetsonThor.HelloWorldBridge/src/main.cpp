#include <arpa/inet.h>
#include <netdb.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <sys/types.h>
#include <unistd.h>

#include <algorithm>
#include <cctype>
#include <cerrno>
#include <chrono>
#include <cstring>
#include <cstdlib>
#include <iostream>
#include <memory>
#include <optional>
#include <sstream>
#include <stdexcept>
#include <string>

namespace
{
struct BridgeConfig
{
    std::string listenHost = "0.0.0.0";
    int listenPort = 7001;
};

struct BridgeRequest
{
    std::string requestId;
    std::string model;
    std::string prompt;
};

class SocketHandle
{
public:
    explicit SocketHandle(int fd = -1) : fd_(fd) {}

    ~SocketHandle()
    {
        if (fd_ >= 0)
        {
            ::close(fd_);
        }
    }

    SocketHandle(const SocketHandle&) = delete;
    SocketHandle& operator=(const SocketHandle&) = delete;

    SocketHandle(SocketHandle&& other) noexcept : fd_(other.fd_)
    {
        other.fd_ = -1;
    }

    SocketHandle& operator=(SocketHandle&& other) noexcept
    {
        if (this != &other)
        {
            if (fd_ >= 0)
            {
                ::close(fd_);
            }

            fd_ = other.fd_;
            other.fd_ = -1;
        }

        return *this;
    }

    int get() const
    {
        return fd_;
    }

private:
    int fd_;
};

void PrintUsage()
{
    std::cout << "jetson_hello_world_bridge [--listen <ip>] [--port <port>]\n";
}

std::string Trim(std::string value)
{
    auto notSpace = [](unsigned char ch) { return !std::isspace(ch); };
    value.erase(value.begin(), std::find_if(value.begin(), value.end(), notSpace));
    value.erase(std::find_if(value.rbegin(), value.rend(), notSpace).base(), value.end());
    return value;
}

std::string ToLower(std::string value)
{
    std::transform(
        value.begin(),
        value.end(),
        value.begin(),
        [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
    return value;
}

std::string JsonEscape(const std::string& value)
{
    std::ostringstream escaped;
    for (unsigned char ch : value)
    {
        switch (ch)
        {
        case '\\':
            escaped << "\\\\";
            break;
        case '"':
            escaped << "\\\"";
            break;
        case '\b':
            escaped << "\\b";
            break;
        case '\f':
            escaped << "\\f";
            break;
        case '\n':
            escaped << "\\n";
            break;
        case '\r':
            escaped << "\\r";
            break;
        case '\t':
            escaped << "\\t";
            break;
        default:
            if (ch < 0x20)
            {
                escaped << "\\u00";
                const char* digits = "0123456789ABCDEF";
                escaped << digits[(ch >> 4) & 0x0F] << digits[ch & 0x0F];
            }
            else
            {
                escaped << static_cast<char>(ch);
            }
            break;
        }
    }

    return escaped.str();
}

unsigned int ParseHex4(const std::string& value, std::size_t start)
{
    if (start + 4 > value.size())
    {
        throw std::runtime_error("Invalid unicode escape.");
    }

    unsigned int code = 0;
    for (std::size_t i = start; i < start + 4; ++i)
    {
        code <<= 4;
        const char ch = value[i];
        if (ch >= '0' && ch <= '9')
        {
            code += static_cast<unsigned int>(ch - '0');
        }
        else if (ch >= 'a' && ch <= 'f')
        {
            code += static_cast<unsigned int>(10 + (ch - 'a'));
        }
        else if (ch >= 'A' && ch <= 'F')
        {
            code += static_cast<unsigned int>(10 + (ch - 'A'));
        }
        else
        {
            throw std::runtime_error("Invalid unicode escape.");
        }
    }

    return code;
}

std::string CodePointToUtf8(unsigned int codePoint)
{
    std::string result;

    if (codePoint <= 0x7F)
    {
        result.push_back(static_cast<char>(codePoint));
    }
    else if (codePoint <= 0x7FF)
    {
        result.push_back(static_cast<char>(0xC0 | ((codePoint >> 6) & 0x1F)));
        result.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
    }
    else if (codePoint <= 0xFFFF)
    {
        result.push_back(static_cast<char>(0xE0 | ((codePoint >> 12) & 0x0F)));
        result.push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F)));
        result.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
    }
    else
    {
        result.push_back(static_cast<char>(0xF0 | ((codePoint >> 18) & 0x07)));
        result.push_back(static_cast<char>(0x80 | ((codePoint >> 12) & 0x3F)));
        result.push_back(static_cast<char>(0x80 | ((codePoint >> 6) & 0x3F)));
        result.push_back(static_cast<char>(0x80 | (codePoint & 0x3F)));
    }

    return result;
}

std::string ParseJsonString(const std::string& json, std::size_t startQuote, std::size_t* endQuote)
{
    std::ostringstream value;

    for (std::size_t i = startQuote + 1; i < json.size(); ++i)
    {
        const char ch = json[i];
        if (ch == '"')
        {
            *endQuote = i;
            return value.str();
        }

        if (ch != '\\')
        {
            value << ch;
            continue;
        }

        if (i + 1 >= json.size())
        {
            throw std::runtime_error("Invalid escape sequence.");
        }

        const char escaped = json[++i];
        switch (escaped)
        {
        case '"':
        case '\\':
        case '/':
            value << escaped;
            break;
        case 'b':
            value << '\b';
            break;
        case 'f':
            value << '\f';
            break;
        case 'n':
            value << '\n';
            break;
        case 'r':
            value << '\r';
            break;
        case 't':
            value << '\t';
            break;
        case 'u':
            value << CodePointToUtf8(ParseHex4(json, i + 1));
            i += 4;
            break;
        default:
            throw std::runtime_error("Unsupported escape sequence.");
        }
    }

    throw std::runtime_error("Unterminated JSON string.");
}

std::optional<std::string> FindJsonStringValue(const std::string& json, const std::string& key)
{
    const auto token = "\"" + key + "\"";
    const auto keyPos = json.find(token);
    if (keyPos == std::string::npos)
    {
        return std::nullopt;
    }

    auto colonPos = json.find(':', keyPos + token.size());
    if (colonPos == std::string::npos)
    {
        return std::nullopt;
    }

    ++colonPos;
    while (colonPos < json.size() && std::isspace(static_cast<unsigned char>(json[colonPos])))
    {
        ++colonPos;
    }

    if (colonPos >= json.size() || json[colonPos] != '"')
    {
        return std::nullopt;
    }

    std::size_t endQuote = std::string::npos;
    return ParseJsonString(json, colonPos, &endQuote);
}

BridgeRequest ParseBridgeRequest(const std::string& json)
{
    BridgeRequest request;
    request.requestId = FindJsonStringValue(json, "requestId").value_or("unknown");
    request.model = FindJsonStringValue(json, "model").value_or("");
    request.prompt = FindJsonStringValue(json, "prompt").value_or("");

    if (request.prompt.empty())
    {
        throw std::runtime_error("Missing 'prompt' field.");
    }

    return request;
}

addrinfo* ResolveAddress(const std::string& host, int port)
{
    addrinfo hints{};
    hints.ai_family = AF_INET;
    hints.ai_socktype = SOCK_STREAM;
    hints.ai_flags = AI_PASSIVE;

    addrinfo* result = nullptr;
    const auto portText = std::to_string(port);
    const auto error = ::getaddrinfo(host.c_str(), portText.c_str(), &hints, &result);
    if (error != 0)
    {
        throw std::runtime_error(std::string("Address resolution failed: ") + gai_strerror(error));
    }

    return result;
}

std::string ReadLineFromClient(int fd)
{
    std::string line;
    char ch = '\0';

    while (true)
    {
        const auto bytesRead = ::recv(fd, &ch, 1, 0);
        if (bytesRead < 0)
        {
            throw std::runtime_error(std::string("Client read failed: ") + std::strerror(errno));
        }

        if (bytesRead == 0)
        {
            break;
        }

        if (ch == '\n')
        {
            break;
        }

        if (ch != '\r')
        {
            line.push_back(ch);
        }

        if (line.size() > 1024 * 1024)
        {
            throw std::runtime_error("Client request exceeded 1 MB.");
        }
    }

    return line;
}

void SendAll(int fd, const std::string& data)
{
    std::size_t totalSent = 0;
    while (totalSent < data.size())
    {
        const auto sent = ::send(fd, data.data() + totalSent, data.size() - totalSent, 0);
        if (sent < 0)
        {
            throw std::runtime_error(std::string("Socket write failed: ") + std::strerror(errno));
        }

        totalSent += static_cast<std::size_t>(sent);
    }
}

std::string BuildBridgeResponse(
    const std::string& requestId,
    const std::string& status,
    const std::string& response,
    const std::string& error,
    long elapsedMs)
{
    std::ostringstream json;
    json
        << "{"
        << "\"requestId\":\"" << JsonEscape(requestId) << "\","
        << "\"status\":\"" << JsonEscape(status) << "\","
        << "\"response\":\"" << JsonEscape(response) << "\","
        << "\"error\":\"" << JsonEscape(error) << "\","
        << "\"elapsedMs\":" << elapsedMs
        << "}\n";

    return json.str();
}

BridgeConfig ParseArgs(int argc, char* argv[])
{
    BridgeConfig config;

    for (int i = 1; i < argc; ++i)
    {
        const std::string arg = argv[i];
        auto requireValue = [&](const std::string& name) -> std::string
        {
            if (i + 1 >= argc)
            {
                throw std::runtime_error("Missing value for " + name);
            }

            return argv[++i];
        };

        if (arg == "--listen")
        {
            config.listenHost = requireValue(arg);
        }
        else if (arg == "--port")
        {
            config.listenPort = std::stoi(requireValue(arg));
        }
        else if (arg == "--help" || arg == "-h")
        {
            PrintUsage();
            std::exit(0);
        }
        else
        {
            throw std::runtime_error("Unknown argument: " + arg);
        }
    }

    return config;
}

std::string BuildHelloWorldReply(const BridgeRequest& request)
{
    const auto prompt = ToLower(Trim(request.prompt));
    if (prompt == "hello")
    {
        return "world";
    }

    return "unsupported";
}

void HandleClient(int clientFd)
{
    const auto startedAt = std::chrono::steady_clock::now();
    std::string requestId = "unknown";

    try
    {
        const auto requestLine = Trim(ReadLineFromClient(clientFd));
        if (requestLine.empty())
        {
            throw std::runtime_error("Client request was empty.");
        }

        const auto request = ParseBridgeRequest(requestLine);
        requestId = request.requestId;
        const auto responseText = BuildHelloWorldReply(request);
        const auto elapsedMs = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - startedAt).count();

        if (responseText == "unsupported")
        {
            SendAll(clientFd, BuildBridgeResponse(requestId, "error", "", "Only 'Hello' heartbeat is supported in this stage.", elapsedMs));
            return;
        }

        SendAll(clientFd, BuildBridgeResponse(requestId, "ok", responseText, "", elapsedMs));
    }
    catch (const std::exception& ex)
    {
        const auto elapsedMs = std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - startedAt).count();
        SendAll(clientFd, BuildBridgeResponse(requestId, "error", "", ex.what(), elapsedMs));
    }
}

SocketHandle CreateServerSocket(const BridgeConfig& config)
{
    std::unique_ptr<addrinfo, decltype(&::freeaddrinfo)> addresses(
        ResolveAddress(config.listenHost, config.listenPort),
        ::freeaddrinfo);

    SocketHandle serverSocket(::socket(addresses->ai_family, addresses->ai_socktype, addresses->ai_protocol));
    if (serverSocket.get() < 0)
    {
        throw std::runtime_error(std::string("Failed to create server socket: ") + std::strerror(errno));
    }

    int reuse = 1;
    if (::setsockopt(serverSocket.get(), SOL_SOCKET, SO_REUSEADDR, &reuse, sizeof(reuse)) < 0)
    {
        throw std::runtime_error(std::string("setsockopt failed: ") + std::strerror(errno));
    }

    if (::bind(serverSocket.get(), addresses->ai_addr, addresses->ai_addrlen) < 0)
    {
        throw std::runtime_error(std::string("bind failed: ") + std::strerror(errno));
    }

    if (::listen(serverSocket.get(), 8) < 0)
    {
        throw std::runtime_error(std::string("listen failed: ") + std::strerror(errno));
    }

    return serverSocket;
}
} // namespace

int main(int argc, char* argv[])
{
    try
    {
        const auto config = ParseArgs(argc, argv);
        auto serverSocket = CreateServerSocket(config);

        std::cout
            << "Jetson hello/world bridge listening on "
            << config.listenHost << ":" << config.listenPort << '\n';

        while (true)
        {
            sockaddr_in clientAddress{};
            socklen_t clientAddressLength = sizeof(clientAddress);

            const auto clientFd = ::accept(
                serverSocket.get(),
                reinterpret_cast<sockaddr*>(&clientAddress),
                &clientAddressLength);

            if (clientFd < 0)
            {
                std::cerr << "accept failed: " << std::strerror(errno) << '\n';
                continue;
            }

            SocketHandle clientSocket(clientFd);
            HandleClient(clientSocket.get());
        }
    }
    catch (const std::exception& ex)
    {
        std::cerr << "Fatal error: " << ex.what() << '\n';
        return 1;
    }
}

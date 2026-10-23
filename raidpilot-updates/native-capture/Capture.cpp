// RaidPilot opt-in WGC bridge. No game process access, injection, network,
// input simulation, screenshot files or external image transfer.
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <memory>
#include <mutex>

namespace {
namespace wgc = winrt::Windows::Graphics::Capture;
namespace dx11 = winrt::Windows::Graphics::DirectX::Direct3D11;
constexpr int kWidth = 160;
constexpr int kHeight = 90;
constexpr int kPixels = kWidth * kHeight;
constexpr auto kFrameInterval = std::chrono::milliseconds(180);

template <typename T>
winrt::com_ptr<T> GetDxgiInterface(winrt::Windows::Foundation::IInspectable const& object)
{
    auto access = object.as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
    winrt::com_ptr<T> result;
    winrt::check_hresult(access->GetInterface(winrt::guid_of<T>(), result.put_void()));
    return result;
}

struct CaptureState final : std::enable_shared_from_this<CaptureState>
{
    std::mutex mutex;
    winrt::com_ptr<ID3D11Device> d3dDevice;
    winrt::com_ptr<ID3D11DeviceContext> d3dContext;
    winrt::com_ptr<ID3D11Texture2D> staging;
    unsigned stagingWidth = 0;
    unsigned stagingHeight = 0;
    wgc::GraphicsCaptureItem item{nullptr};
    wgc::Direct3D11CaptureFramePool framePool{nullptr};
    wgc::GraphicsCaptureSession session{nullptr};
    winrt::event_token frameToken{};
    bool eventAttached = false;
    bool stopped = false;
    std::array<std::uint8_t, kPixels> luminance{};
    std::uint64_t sequence = 0;
    std::chrono::steady_clock::time_point lastSample{};

    bool Start(HWND window)
    {
        if (!IsWindow(window) || IsIconic(window) || !wgc::GraphicsCaptureSession::IsSupported())
            return false;

        winrt::check_hresult(D3D11CreateDevice(
            nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr, 0, D3D11_SDK_VERSION,
            d3dDevice.put(), nullptr, d3dContext.put()));

        auto dxgi = d3dDevice.as<IDXGIDevice>();
        winrt::com_ptr<::IInspectable> inspectable;
        winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(
            dxgi.get(), inspectable.put()));
        auto winrtDevice = inspectable.as<dx11::IDirect3DDevice>();

        auto factory = winrt::get_activation_factory<
            wgc::GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        winrt::check_hresult(factory->CreateForWindow(
            window,
            winrt::guid_of<ABI::Windows::Graphics::Capture::IGraphicsCaptureItem>(),
            reinterpret_cast<void**>(winrt::put_abi(item))));

        const auto size = item.Size();
        if (size.Width < kWidth || size.Height < kHeight
            || size.Width > 16384 || size.Height > 16384)
            return false;

        framePool = wgc::Direct3D11CaptureFramePool::CreateFreeThreaded(
            winrtDevice,
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            2, size);

        std::weak_ptr<CaptureState> weak = weak_from_this();
        frameToken = framePool.FrameArrived([weak](
            wgc::Direct3D11CaptureFramePool const& sender,
            winrt::Windows::Foundation::IInspectable const&)
        {
            if (auto state = weak.lock()) state->OnFrame(sender);
        });
        eventAttached = true;
        session = framePool.CreateCaptureSession(item);
        // Keep Windows' visible capture border/indicator.
        session.StartCapture();
        return true;
    }

    void OnFrame(wgc::Direct3D11CaptureFramePool const& sender) noexcept
    {
        try
        {
            auto frame = sender.TryGetNextFrame();
            if (!frame) return;
            std::lock_guard guard(mutex);
            if (stopped) return;
            auto now = std::chrono::steady_clock::now();
            if (lastSample.time_since_epoch().count() != 0
                && (now - lastSample) < kFrameInterval) return;
            lastSample = now;

            auto texture = GetDxgiInterface<ID3D11Texture2D>(frame.Surface());
            D3D11_TEXTURE2D_DESC description{};
            texture->GetDesc(&description);
            if (description.Format != DXGI_FORMAT_B8G8R8A8_UNORM
                || description.SampleDesc.Count != 1
                || description.Width < kWidth || description.Height < kHeight
                || description.Width > 16384 || description.Height > 16384) return;

            if (!staging || stagingWidth != description.Width
                || stagingHeight != description.Height)
            {
                D3D11_TEXTURE2D_DESC stage = description;
                stage.Usage = D3D11_USAGE_STAGING;
                stage.BindFlags = 0;
                stage.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
                stage.MiscFlags = 0;
                stage.MipLevels = 1;
                stage.ArraySize = 1;
                staging = nullptr;
                winrt::check_hresult(d3dDevice->CreateTexture2D(
                    &stage, nullptr, staging.put()));
                stagingWidth = description.Width;
                stagingHeight = description.Height;
            }

            d3dContext->CopyResource(staging.get(), texture.get());
            D3D11_MAPPED_SUBRESOURCE mapped{};
            winrt::check_hresult(d3dContext->Map(
                staging.get(), 0, D3D11_MAP_READ, 0, &mapped));
            try
            {
                const unsigned w = description.Width;
                const unsigned h = description.Height;
                const unsigned left = static_cast<unsigned>(w * .08);
                const unsigned top = static_cast<unsigned>(h * .12);
                const unsigned cropW = std::max(1u, static_cast<unsigned>(w * .84));
                const unsigned cropH = std::max(1u, static_cast<unsigned>(h * .68));
                for (int y = 0; y < kHeight; ++y)
                {
                    const unsigned sy = std::min(h - 1,
                        top + static_cast<unsigned>(y) * cropH / kHeight);
                    const auto row = static_cast<std::uint8_t const*>(mapped.pData)
                        + static_cast<std::size_t>(sy) * mapped.RowPitch;
                    for (int x = 0; x < kWidth; ++x)
                    {
                        const unsigned sx = std::min(w - 1,
                            left + static_cast<unsigned>(x) * cropW / kWidth);
                        const auto pixel = row + static_cast<std::size_t>(sx) * 4;
                        luminance[static_cast<std::size_t>(y) * kWidth + x] =
                            static_cast<std::uint8_t>(
                                (static_cast<unsigned>(pixel[2]) * 54
                                + static_cast<unsigned>(pixel[1]) * 183
                                + static_cast<unsigned>(pixel[0]) * 19 + 128) >> 8);
                    }
                }
                ++sequence;
            }
            catch (...)
            {
                d3dContext->Unmap(staging.get(), 0);
                throw;
            }
            d3dContext->Unmap(staging.get(), 0);
        }
        catch (...)
        {
            // Unsupported capture/format must never take down RaidPilot.
        }
    }

    void Stop() noexcept
    {
        {
            std::lock_guard guard(mutex);
            if (stopped) return;
            stopped = true;
        }
        try { if (eventAttached && framePool) framePool.FrameArrived(frameToken); }
        catch (...) {}
        try { if (session) session.Close(); }
        catch (...) {}
        try { if (framePool) framePool.Close(); }
        catch (...) {}
        session = nullptr;
        framePool = nullptr;
        item = nullptr;
    }
    ~CaptureState() { Stop(); }
};

struct NativeCapture
{
    std::shared_ptr<CaptureState> state;
    std::uint64_t lastRead = 0;
    ~NativeCapture() { if (state) state->Stop(); }
};
}

extern "C" __declspec(dllexport) int __cdecl rp_wgc_version() noexcept
{
    return 1;
}

extern "C" __declspec(dllexport) void* __cdecl rp_wgc_start(HWND window) noexcept
{
    if (!IsWindow(window) || IsIconic(window)) return nullptr;
    try
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        auto handle = std::make_unique<NativeCapture>();
        handle->state = std::make_shared<CaptureState>();
        if (!handle->state->Start(window)) return nullptr;
        return handle.release();
    }
    catch (...) { return nullptr; }
}

// 1=fresh luminance (160x90), 0=no new frame, -1=bad call.
extern "C" __declspec(dllexport) int __cdecl rp_wgc_poll(
    void* nativeHandle, std::uint8_t* dst, int capacity,
    std::uint64_t* sequence) noexcept
{
    if (!nativeHandle || !dst || capacity < kPixels || !sequence) return -1;
    auto* handle = static_cast<NativeCapture*>(nativeHandle);
    auto state = handle->state;
    if (!state) return -1;
    std::lock_guard guard(state->mutex);
    if (state->stopped) return -1;
    if (state->sequence == 0 || state->sequence == handle->lastRead)
        return 0;
    std::memcpy(dst, state->luminance.data(), kPixels);
    handle->lastRead = state->sequence;
    *sequence = state->sequence;
    return 1;
}

extern "C" __declspec(dllexport) void __cdecl rp_wgc_stop(void* nativeHandle) noexcept
{
    try { delete static_cast<NativeCapture*>(nativeHandle); }
    catch (...) {}
}

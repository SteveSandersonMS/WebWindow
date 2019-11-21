#pragma once

#include <combaseapi.h>

namespace AsyncOpHelpers
{
    template <typename TIOperationResult, typename TIResults>
    HRESULT WaitForCompletionAndGetResults(
        ABI::Windows::Foundation::IAsyncOperation<TIOperationResult>* operationIn,
        TIResults* results)
    {
        typedef ABI::Windows::Foundation::IAsyncOperation<TIOperationResult> TIOperation;
        typedef ABI::Windows::Foundation::IAsyncOperationCompletedHandler<TIOperationResult> TIDelegate;

        class EventDelegate :
            public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::RuntimeClassType::Delegate>,
                                                TIDelegate,
                                                Microsoft::WRL::FtmBase>
        {
        public:
            EventDelegate()
                : _status(AsyncStatus::Started), _hEventCompleted(nullptr)
            {
            }

            HRESULT RuntimeClassInitialize()
            {
                _hEventCompleted = CreateEventEx(nullptr, nullptr, 0, EVENT_ALL_ACCESS);

                return _hEventCompleted == nullptr ? HRESULT_FROM_WIN32(GetLastError()) : S_OK;
            }


            HRESULT __stdcall Invoke(
                _In_ TIOperation*,
                _In_ AsyncStatus status)
            {
                _status = status;
                SetEvent(_hEventCompleted);
                return S_OK;
            }

            HANDLE GetEvent()
            {
                return _hEventCompleted;
            }

            AsyncStatus GetStatus()
            {
                return _status;
            }

        private:
            ~EventDelegate()
            {
                CloseHandle(_hEventCompleted);
            }

            AsyncStatus _status;
            HANDLE _hEventCompleted;
        };


        Microsoft::WRL::ComPtr<TIOperation> operation = operationIn;
        Microsoft::WRL::ComPtr<EventDelegate> eventCallback;

        HRESULT hr = Microsoft::WRL::MakeAndInitialize<EventDelegate>(&eventCallback);


        if (SUCCEEDED(hr))
        {
            hr = operation->put_Completed(eventCallback.Get());

            if (SUCCEEDED(hr))
            {
                HANDLE waitForEvents[1] = {eventCallback->GetEvent()};
                DWORD handleCount = ARRAYSIZE(waitForEvents);
                DWORD handleIndex = 0;
                HRESULT hr = CoWaitForMultipleHandles(0, INFINITE, handleCount, waitForEvents, &handleIndex);

                if (SUCCEEDED(hr) && handleIndex != 0)
                {
                    hr = HRESULT_FROM_WIN32(ERROR_CANCELLED);
                }

                if (SUCCEEDED(hr))
                {
                    if (eventCallback->GetStatus() == AsyncStatus::Completed)
                    {
                        hr = operation->GetResults(results);
                    }
                    else
                    {
                        Microsoft::WRL::ComPtr<IAsyncInfo> asyncInfo;

                        if (SUCCEEDED(operation->QueryInterface(IID_PPV_ARGS(&asyncInfo))))
                        {
                            asyncInfo->get_ErrorCode(&hr);
                        }
                    }
                }
            }
        }

        return hr;
    }
}

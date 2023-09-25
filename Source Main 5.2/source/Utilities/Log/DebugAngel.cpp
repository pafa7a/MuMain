// DebugAngel.cpp: implementation of the DebugAngel class.
//////////////////////////////////////////////////////////////////////

#include "stdafx.h"
//#include <dxerr8.h>
#include "DebugAngel.h"

void WriteDebugInfoStr(char* lpszFileName, char* lpszToWrite)
{
    HANDLE hFile = CreateFile(lpszFileName, GENERIC_WRITE, FILE_SHARE_READ, NULL, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    SetFilePointer(hFile, 0, NULL, FILE_END);
    DWORD dwNumber;
    WriteFile(hFile, lpszToWrite, strlen(lpszToWrite), &dwNumber, NULL);
    CloseHandle(hFile);
}

void DebugAngel_Write(char* lpszFileName, ...)
{
    char lpszBuffer[1024];
    va_list va;
    va_start(va, lpszFileName);
    char* lpszFormat = va_arg(va, char*);
    wvsprintf(lpszBuffer, lpszFormat, va);
    WriteDebugInfoStr(lpszFileName, lpszBuffer);
    va_end(va);
}

void GetCurrentTimeWrapped(char* timeStr) {
    // Get the current time
    time_t currentTime;
    time(&currentTime);

    // Convert the time to a string
    std::strftime(timeStr, 64, "[%Y-%m-%d %H:%M:%S] ", std::localtime(&currentTime));
}


void DebugAngel_HexWrite(char* lpszFileName, void* pBuffer, int iSize, int sender) {
    HANDLE hFile = CreateFile(lpszFileName, GENERIC_WRITE, FILE_SHARE_READ, NULL, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    SetFilePointer(hFile, 0, NULL, FILE_END);
    DWORD dwNumber;

    BYTE* pbySeek = (BYTE*)pBuffer;
    char lpszStr[1024];
    char logType[10] = "";
    char timeStr[64];

    lpszStr[0] = '\0';

    // Get the current time wrapped in square brackets
    GetCurrentTimeWrapped(timeStr);
    strcat(lpszStr, timeStr);

    // Append logType
    if (sender == 1) {
        strcpy(logType, "C->S ");
    }
    else {
        strcpy(logType, "S->C ");
    }
    strcat(lpszStr, logType);

    // Hex Ãâ·Â
    for (int j = 0; j < iSize; j++, pbySeek++) {
        char lpszTemp[16];
        wsprintf(lpszTemp, "0x%02X ", *pbySeek);
        strcat(lpszStr, lpszTemp);
    }

    strcat(lpszStr, "\r\n");

    WriteFile(hFile, lpszStr, strlen(lpszStr), &dwNumber, NULL);

    CloseHandle(hFile);
}

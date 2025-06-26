#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
WiFi Portal自动认证程序打包脚本
"""

import os
import sys
import subprocess
import shutil

def install_pyinstaller():
    """安装PyInstaller"""
    print("正在安装PyInstaller...")
    try:
        subprocess.check_call([sys.executable, "-m", "pip", "install", "pyinstaller"])
        print("PyInstaller安装成功！")
        return True
    except subprocess.CalledProcessError:
        print("PyInstaller安装失败！")
        return False

def build_exe():
    """打包为exe文件"""
    print("开始打包程序...")
    
    # PyInstaller命令参数
    cmd = [
        "pyinstaller",
        "--onefile",                    # 打包为单个exe文件
        "--noconsole",                  # 不显示控制台窗口（如需调试可删除此行）
        "--name=WiFi自动认证工具",       # exe文件名
        "--icon=icon.ico",              # 图标文件（如果存在）
        "--add-data=config.txt;.",      # 包含配置文件模板
        "--hidden-import=pywifi",       # 确保pywifi被包含
        "--hidden-import=selenium",     # 确保selenium被包含
        "--hidden-import=configparser", # 确保configparser被包含
        "--hidden-import=shutil",       # 确保shutil被包含
        "--exclude-module=tkinter",     # 排除不需要的模块
        "--exclude-module=matplotlib",  # 排除不需要的模块
        "wifi_portal_selenium.py"       # 主程序文件
    ]
    
    # 如果没有图标文件，移除图标参数
    if not os.path.exists("icon.ico"):
        cmd.remove("--icon=icon.ico")
    
    # 如果没有配置文件，移除配置文件参数
    if not os.path.exists("config.txt"):
        cmd.remove("--add-data=config.txt;.")
    
    try:
        subprocess.check_call(cmd)
        print("打包成功！")
        return True
    except subprocess.CalledProcessError as e:
        print(f"打包失败: {e}")
        return False

def cleanup_build_files():
    """清理打包过程中的临时文件"""
    cleanup_dirs = ["build", "__pycache__", "edge_temp_profile_1", "edge_temp_profile_2"]
    cleanup_files = ["*.spec"]
    
    for dir_name in cleanup_dirs:
        if os.path.exists(dir_name):
            try:
                shutil.rmtree(dir_name)
                print(f"已清理: {dir_name}")
            except Exception as e:
                print(f"清理 {dir_name} 失败: {e}")
    
    import glob
    for pattern in cleanup_files:
        for file_path in glob.glob(pattern):
            try:
                os.remove(file_path)
                print(f"已清理: {file_path}")
            except Exception as e:
                print(f"清理 {file_path} 失败: {e}")

def copy_files():
    """复制必要文件到dist目录"""
    dist_dir = "dist"
    if os.path.exists(dist_dir):
        # 复制msedgedriver.exe（如果存在）
        if os.path.exists("msedgedriver.exe"):
            shutil.copy2("msedgedriver.exe", dist_dir)
            print("已复制msedgedriver.exe到dist目录")
        
        # 复制配置文件模板（如果存在）
        if os.path.exists("config.txt"):
            shutil.copy2("config.txt", dist_dir)
            print("已复制config.txt到dist目录")
        
        print(f"\n打包完成！可执行文件位于: {os.path.abspath(dist_dir)}")
        print("使用说明：")
        print("1. 将msedgedriver.exe放入exe文件同目录")
        print("2. 首次运行会生成config.txt配置文件")
        print("3. 修改config.txt中的用户名和密码")
        print("4. 再次运行即可自动认证")
        print("5. 请以管理员权限运行程序")

def main():
    """主函数"""
    print("WiFi Portal自动认证程序 - 打包工具")
    print("=" * 50)
    
    # 检查主程序文件是否存在
    if not os.path.exists("wifi_portal_selenium.py"):
        print("错误：找不到wifi_portal_selenium.py文件！")
        return
    
    # 清理旧的临时文件
    cleanup_build_files()
    
    # 安装PyInstaller
    if not install_pyinstaller():
        return
    
    # 打包程序
    if build_exe():
        copy_files()
        cleanup_build_files()  # 清理打包临时文件
    else:
        print("打包失败，请检查错误信息")

if __name__ == "__main__":
    main() 
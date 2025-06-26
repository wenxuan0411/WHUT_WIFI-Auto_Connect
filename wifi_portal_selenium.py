from selenium import webdriver
from selenium.webdriver.edge.service import Service
from selenium.webdriver.common.by import By
from selenium.webdriver.common.keys import Keys
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.common.exceptions import TimeoutException, UnexpectedAlertPresentException
import time
import logging
import pywifi
from pywifi import const
import os
import configparser
import sys
import shutil

# 获取脚本所在文件夹路径（支持打包后的exe）
if getattr(sys, 'frozen', False):
    # 如果是打包后的exe文件
    SCRIPT_DIR = os.path.dirname(sys.executable)
else:
    # 如果是Python脚本
    SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
EDGE_DRIVER_PATH = os.path.join(SCRIPT_DIR, "msedgedriver.exe")
CONFIG_FILE_PATH = os.path.join(SCRIPT_DIR, "config.txt")

BASE_PORTAL_URL = "http://172.30.21.100"
WIFI_SSID = "WHUT-DORM"

logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')

def create_default_config():
    """创建默认配置文件"""
    config = configparser.ConfigParser()
    config['SETTINGS'] = {
        'username': '',
        'password': '',
        'portal_url': 'auto',  # auto表示自动获取，也可以填写固定URL
        'wifi_ssid': 'WHUT-DORM'
    }
    
    with open(CONFIG_FILE_PATH, 'w', encoding='utf-8') as f:
        config.write(f)
    
    logging.info(f"已创建默认配置文件: {CONFIG_FILE_PATH}")
    logging.info("请根据需要修改配置文件中的用户名和密码")

def load_config():
    """加载配置文件"""
    if not os.path.exists(CONFIG_FILE_PATH):
        create_default_config()
    
    config = configparser.ConfigParser()
    config.read(CONFIG_FILE_PATH, encoding='utf-8')
    
    try:
        username = config['SETTINGS']['username']
        password = config['SETTINGS']['password'] 
        portal_url = config['SETTINGS']['portal_url']
        wifi_ssid = config['SETTINGS'].get('wifi_ssid', 'WHUT-DORM')
        
        logging.info(f"已加载配置: 用户名={username}, WiFi={wifi_ssid}")
        return username, password, portal_url, wifi_ssid
    except KeyError as e:
        logging.error(f"配置文件格式错误，缺少字段: {e}")
        return None, None, None, None

def check_edge_driver():
    """检查Edge驱动是否存在"""
    if os.path.exists(EDGE_DRIVER_PATH):
        logging.info(f"找到Edge驱动: {EDGE_DRIVER_PATH}")
        return True
    else:
        logging.error(f"未找到Edge驱动: {EDGE_DRIVER_PATH}")
        logging.error("请将msedgedriver.exe放在脚本同文件夹内")
        return False

def connect_wifi(wifi_ssid):
    """连接到指定的WiFi开放网络"""
    wifi = pywifi.PyWiFi()
    iface = wifi.interfaces()[0]
    
    logging.info(f"正在连接到 {wifi_ssid}")
    iface.disconnect()
    time.sleep(1)

    profile = pywifi.Profile()
    profile.ssid = wifi_ssid
    profile.auth = const.AUTH_ALG_OPEN
    profile.akm.append(const.AKM_TYPE_NONE)
    profile.cipher = const.CIPHER_TYPE_NONE

    iface.remove_all_network_profiles()
    tmp_profile = iface.add_network_profile(profile)
    iface.connect(tmp_profile)
    time.sleep(2)

    if iface.status() == const.IFACE_CONNECTED:
        logging.info("WiFi连接成功")
        return True
    else:
        logging.error("WiFi连接失败")
        return False

def get_portal_url():
    """获取Portal认证页面的完整URL"""
    service = Service(EDGE_DRIVER_PATH)
    options = webdriver.EdgeOptions()
    options.add_argument('--edge-skip-compat-layer-relaunch')  # 解决打包后兼容性问题
    options.add_argument('--disable-gpu')
    options.add_argument('--ignore-certificate-errors')
    options.add_argument('--ignore-ssl-errors')
    options.add_argument('--no-sandbox')
    options.add_argument('--disable-dev-shm-usage')
    options.add_argument(f'--user-data-dir={os.path.join(SCRIPT_DIR, "edge_temp_profile_1")}')
    driver = webdriver.Edge(service=service, options=options)
    
    try:
        # 访问基础URL，让系统自动跳转到Portal页面
        driver.get(BASE_PORTAL_URL)
        time.sleep(3)
        
        current_url = driver.current_url
        logging.info(f"获取到Portal URL: {current_url}")
        
        # 检查是否包含必要的参数
        if "login.html" in current_url and ("ip=" in current_url or "acip=" in current_url):
            return current_url
        else:
            logging.warning("未获取到正确的Portal URL，尝试直接访问登录页面")
            return f"{BASE_PORTAL_URL}/tpl/whut/login.html"
            
    except Exception as e:
        logging.error(f"获取Portal URL时出错: {str(e)}")
        return None
    finally:
        driver.quit()

def handle_alert(driver):
    """处理页面弹窗"""
    try:
        alert = driver.switch_to.alert
        alert_text = alert.text
        logging.warning(f"检测到弹窗: {alert_text}")
        alert.accept()
        return True
    except:
        return False

def portal_login(portal_url, username, password, max_retries=1):
    """使用完整URL进行Portal登录，支持重试"""
    for attempt in range(max_retries):
        logging.info(f"第 {attempt + 1} 次尝试登录...")
        
        service = Service(EDGE_DRIVER_PATH)
        options = webdriver.EdgeOptions()
        options.add_argument('--edge-skip-compat-layer-relaunch')  # 解决打包后兼容性问题
        #options.add_argument('--headless')  # 如需可视化可注释掉本行
        options.add_argument('--disable-gpu')
        options.add_argument('--ignore-certificate-errors')
        options.add_argument('--ignore-ssl-errors')
        options.add_argument('--no-sandbox')
        options.add_argument('--disable-dev-shm-usage')
        options.add_argument(f'--user-data-dir={os.path.join(SCRIPT_DIR, "edge_temp_profile_2")}')
        driver = webdriver.Edge(service=service, options=options)
        
        try:
            driver.get(portal_url)
            time.sleep(3)

            # 处理可能的弹窗
            handle_alert(driver)

            # 等待页面元素加载
            wait = WebDriverWait(driver, 10)
            
            # 输入用户名
            username_input = wait.until(EC.presence_of_element_located((By.NAME, "username")))
            username_input.clear()
            username_input.send_keys(username)

            # 输入密码
            password_input = wait.until(EC.presence_of_element_located((By.NAME, "password")))
            password_input.clear()
            password_input.send_keys(password)

            # 点击登录按钮
            login_btn = wait.until(EC.element_to_be_clickable((By.ID, "login-account")))
            login_btn.click()
            logging.info("已点击登录按钮，等待跳转...")
            
            # 等待可能的弹窗
            time.sleep(1)
            handle_alert(driver)
            
            time.sleep(2)

            # 检查是否跳转到success.html
            current_url = driver.current_url
            if "success.html" in current_url:
                logging.info("登录成功，已跳转到success.html！")
                return True
            else:
                logging.warning(f"未检测到跳转，当前页面: {current_url}")
                # 保存当前页面源码
                with open(f"portal_login_result_attempt_{attempt+1}.html", "w", encoding="utf-8") as f:
                    f.write(driver.page_source)
                logging.info(f"已保存当前页面源码为 portal_login_result_attempt_{attempt+1}.html")
                
                # 如果不是最后一次尝试，等待后重试
                if attempt < max_retries - 1:
                    logging.info("等待5秒后重试...")
                    time.sleep(3)
                    
        except UnexpectedAlertPresentException:
            alert_text = driver.switch_to.alert.text
            logging.error(f"登录过程中出现弹窗: {alert_text}")
            driver.switch_to.alert.accept()
            if "IP不在线" in alert_text:
                logging.error("IP不在线错误，可能需要重新连接网络")
                return False
        except Exception as e:
            logging.error(f"第 {attempt + 1} 次尝试出错: {str(e)}")
        finally:
            driver.quit()
    
    logging.error(f"所有 {max_retries} 次尝试均失败")
    return False

def cleanup_temp_profiles():
    """清理Edge临时配置文件"""
    temp_profiles = [
        os.path.join(SCRIPT_DIR, "edge_temp_profile_1"),
        os.path.join(SCRIPT_DIR, "edge_temp_profile_2")
    ]
    
    for profile_dir in temp_profiles:
        if os.path.exists(profile_dir):
            try:
                shutil.rmtree(profile_dir)
                logging.info(f"已清理临时配置文件: {profile_dir}")
            except Exception as e:
                logging.warning(f"清理临时配置文件失败: {e}")

def main():
    """主流程：检查驱动 -> 加载配置 -> 连接WiFi -> 获取Portal URL -> 自动登录"""
    # 0. 清理临时文件
    cleanup_temp_profiles()
    
    # 1. 检查Edge驱动
    if not check_edge_driver():
        return
    
    # 2. 加载配置
    username, password, portal_url_config, wifi_ssid = load_config()
    if not username or not password:
        logging.error("配置文件加载失败，请检查config.txt文件")
        return
    
    # 3. 连接WiFi
    if not connect_wifi(wifi_ssid):
        logging.error("WiFi连接失败，退出程序")
        return
    
    # 4. 等待网络稳定
    logging.info("等待网络稳定...")
    time.sleep(2)
    
    # 5. 获取Portal URL（如果配置文件中设置为auto）
    if portal_url_config.lower() == 'auto':
        portal_url = get_portal_url()
        if not portal_url:
            logging.error("无法获取Portal URL，退出程序")
            return
    else:
        portal_url = portal_url_config
        logging.info(f"使用配置文件中的Portal URL: {portal_url}")
    
    # 6. 自动登录
    success = portal_login(portal_url, username, password)
    if success:
        logging.info("Portal认证完成！")
    else:
        logging.error("Portal认证失败")
    
    # 7. 清理临时文件
    cleanup_temp_profiles()

if __name__ == "__main__":
    main() 
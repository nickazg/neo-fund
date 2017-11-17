import os
import sys
import json
import time
import datetime
import argparse
import binascii
import traceback

from neo.Implementations.Wallets.peewee.UserWallet import UserWallet
from neo.Implementations.Blockchains.LevelDB.LevelDBBlockchain import LevelDBBlockchain
from neo.Wallets.KeyPair import KeyPair
from neo.Prompt.Commands.LoadSmartContract import ImportMultiSigContractAddr
from neo.Prompt.Commands.Invoke import InvokeContract, TestInvokeContract, test_invoke
from neo.Prompt.Commands.Withdraw import RequestWithdraw, RedeemWithdraw, construct_withdrawal_tx, get_contract_holds_for_address
from neo.Prompt.Commands.LoadSmartContract import LoadContract, GatherContractDetails, ImportContractAddr, ImportMultiSigContractAddr, generate_deploy_script
from neo.Prompt.Commands.Send import construct_and_send, construct_contract_withdrawal, parse_and_sign
from neo.Prompt.Commands.Wallet import DeleteAddress, ImportWatchAddr
from neo.Prompt.Notify import SubscribeNotifications
from neo.Prompt.Utils import parse_param, get_arg, get_asset_id, get_asset_amount, get_withdraw_from_watch_only, parse_hold_vins
from neo.Core.Blockchain import Blockchain
from neo.Fixed8 import Fixed8
from neo.Cryptography.Crypto import Crypto
from neo.Core.TX.Transaction import TransactionOutput,ContractTransaction
from neo.Core.TX.InvocationTransaction import InvocationTransaction
from neo.SmartContract.ContractParameterContext import ContractParametersContext
from neo.Network.NodeLeader import NodeLeader
from twisted.internet import reactor, task
from neo.Settings import settings
from neo.Cryptography.Helper import scripthash_to_address
from neo.Implementations.Wallets.peewee.Models import Address
from neo.UInt160 import UInt160



from prompt_toolkit import prompt
from prompt_toolkit.styles import style_from_dict
from prompt_toolkit.shortcuts import print_tokens
from prompt_toolkit.token import Token
from prompt_toolkit.contrib.completers import WordCompleter
from prompt_toolkit.history import FileHistory

wallet_addr = 'AZBRYPe4n5B34Ca59Yig4M88M7qz2jDrdf'
test_pub_addr = '03cbcc8aee11bdfeff365827e24d932af6f833819d14e468acd9692ba2dffc53c4'

neo_fund_avm = '/Users/nick/Documents/Git/NeoDev/neo-fund/neo-fund-sc/neo-fund-sc/bin/Debug/neo-fund-sc.avm'
neo_fund_sc = '64da1df94e1321e767ea1a62322957ebddcfaaef'
python_wallet = '/Users/nick/Documents/Git/NeoDev/pythonWalletDEV.db3'
pythong_wallet_pass = 'pythonwallet'
test_fund = 'testFund2'

neo_asset_id_hex = 'c56f33fc6ecfcd0c225c4ab356fee59390af8560be0e930faebe74a6daff7c9b'
neo_asset_id_bytes = bytearray(b'\x9b|\xff\xda\xa6t\xbe\xae\x0f\x93\x0e\xbe`\x85\xaf\x90\x93\xe5\xfeV\xb3J\\"\x0c\xcd\xcfn\xfc3o\xc5')

gas_asset_id_hex = '602c79718b16e442de58778e148d0b1084e3b2dffd5de6b7b16cee7969282de7'

class NeoFund:
    def __init__(self, walletpath, walletpass, operation, params=None, deploy=False):
        self.start_height = Blockchain.Default().Height
        self.start_dt = datetime.datetime.utcnow()
        self.walletpath = walletpath
        self.walletpass = walletpass
        self.Wallet = None
        self.operation = operation
        self.neo_fund_sc = ''
        self.neo_fund_sc_addr = ''
        self.contract_script = None
        self.params = params
        self.go_on = True
        self._walletdb_loop = None
        self.deploy = deploy
        self._known_addresses = []
        self.contract_addr = None
        self.history = FileHistory('.prompt.py.history')

        self.token_style = style_from_dict({
            Token.Command: settings.token_style['Command'],
            Token.Neo: settings.token_style['Neo'],
            Token.Default: settings.token_style['Default'],
            Token.Number: settings.token_style['Number'],
        })

    def quit(self):
        print('Shutting down.  This may take a bit...')
        self.go_on = False
        Blockchain.Default().Dispose()
        reactor.stop()
        NodeLeader.Instance().Shutdown()

    def show_tx(self, args):
        item = get_arg(args)
        if item is not None:
            try:
                tx, height = Blockchain.Default().GetTransaction(item)
                if height > -1:

                    bjson = json.dumps(tx.ToJson(), indent=4)
                    tokens = [(Token.Command, bjson)]
                    print_tokens(tokens, self.token_style)
                    print('\n')
            except Exception as e:
                print("Could not find transaction with id %s " % item)
                print("Please specify a tx hash like 'db55b4d97cf99db6826967ef4318c2993852dff3e79ec446103f141c716227f6'")
        else:
            print("please specify a tx hash")


    def createWallet(self):
        # Creating wallet instance
        self.Wallet = UserWallet.Open(path=self.walletpath, password=self.walletpass)
        print("AddressVersion: ", self.Wallet.AddressVersion)
        self._walletdb_loop = task.LoopingCall(self.Wallet.ProcessBlocks)
        self._walletdb_loop.start(1)

        self.wallet_sh = self.Wallet.GetStandardAddress()
        self.wallet_addr = Crypto.ToAddress(self.wallet_sh)
        for addr in self.Wallet.ToJson()['public_keys']:
            if addr['Address'] == self.wallet_addr:
                self.wallet_pub = addr['Public Key']

        print('Wallet SH', self.wallet_sh)
        print('Wallet Addr', self.wallet_addr)
        print('Wallet Pub', self.wallet_pub)
        # for addr in Address.select():
        #     print('SHH', addr.ScriptHash)
        #     print('SHH str', Crypto.ToAddress(UInt160(data=addr.ScriptHash)))


    def show_wallet(self, arguments):
        print("Wallet %s " % json.dumps(self.Wallet.ToJson(), indent=4))

    def invokeDeploy(self):
        if self.contract_script:
            tx, fee, results, num_ops = test_invoke(self.contract_script, self.Wallet, [])
            print("tx", tx.Hash)
            print("tx.inputs", tx.inputs)
            print("fee", fee)
            InvokeContract(self.Wallet, tx, fee)

            print('\nDEPLOY', results)
            print('new_sc:', self.neo_fund_sc)
            print('deploing Contract...')

    def autoDeploy(self):
        self.Wallet.Rebuild()
        function_code = LoadContract([neo_fund_avm, '0710', '05', 'True'])
        self.contract_script = generate_deploy_script(
            function_code.Script,
            'NeoFund',
            str(int(time.time())),
            'Nick',
            'nickazg@gmail.com',
            'auto deploy',
            True,
            ord(function_code.ReturnType),
            function_code.ParameterList
        )

        if self.contract_script is not None:
            self.neo_fund_sc = function_code.ToJson()['hash']
            # self.neo_fund_sc_addr = ImportContractAddr(self.Wallet, [self.neo_fund_sc, test_pub_addr]).Address

            print('SC Hash: ',  self.neo_fund_sc)
            # print('SC Addr: ',  self.neo_fund_sc_addr)


        return self.contract_script

    def get_completer(self):
        standard_completions = ['send_test_funds','withdrawRequestReset', 'withdrawRequestFunds', 'deleteAddr','isRefundActive', 'importContract', 'withdrawFunds', 'wallet','createFund', 'invokeDeploy', 'depositFunds', 'getFundParameter', 'quit','help','tx']

        if self.Wallet:
            for addr in self.Wallet.Addresses:
                if addr not in self._known_addresses:
                    self._known_addresses.append(addr)

        all_completions = standard_completions + self._known_addresses

        completer = WordCompleter(all_completions)

        return completer

    def get_bottom_toolbar(self, cli=None):
        out = []
        try:
            out = [(Token.Command, '[%s] Progress: ' % settings.net_name),
                   (Token.Number, str(Blockchain.Default().Height)),
                   (Token.Neo, '/'),
                   (Token.Number, str(Blockchain.Default().HeaderHeight))]
        except Exception as e:
            pass

        return out

    def parse_result(self, result):
        if len(result):
            commandParts = [s for s in result.split()]
            return commandParts[0], commandParts[1:]
        return None, None

    def runPrompt(self):

        dbloop = task.LoopingCall(Blockchain.Default().PersistBlocks)
        dbloop.start(.1)

        Blockchain.Default().PersistBlocks()

        self.createWallet()
        self.autoDeploy()

        print("\n")

        # self.withdrawFunds(self.Wallet)
        # self.exit()

        while self.go_on:

            try:
                result = prompt("neoFund> ",
                                completer=self.get_completer(),
                                history=self.history,
                                get_bottom_toolbar_tokens=self.get_bottom_toolbar,
                                style=self.token_style,
                                refresh_interval=.5)
            except EOFError:
                # Control-D pressed: quit
                return self.quit()
            except KeyboardInterrupt:
                # Control-C pressed: do nothing
                continue
            try:
                command, arguments = self.parse_result(result)

                if command is not None and len(command) > 0:
                    # command = command.lower()
                    print(command)
                    if command == 'quit' or command == 'exit':
                        self.quit()
                    elif command == 'help':
                        self.help()
                    elif command == 'tx':
                        self.show_tx(arguments)
                    elif command == 'wallet':
                        self.show_wallet(arguments)
                    elif command == 'invokeDeploy':
                        self.invokeDeploy()
                    elif command == 'createFund':
                        self.createFund(self.Wallet, test_fund, 'neo', 'withdrawal_SH', 100, 9999)
                    elif command == 'getFundParameter':
                        self.getFundParameter(self.Wallet, test_fund, 'fundBalance')
                    elif command == 'depositFunds':
                        self.depositFunds(self.Wallet, test_fund, 'neo', 5 , args=arguments)
                    elif command == 'withdrawFunds':
                        self.withdrawFunds(self.Wallet, 1, args=arguments)
                    elif command == 'withdrawRequestFunds':
                        self.withdrawRequestFunds(self.Wallet, test_fund, 1, args=arguments)
                    elif command == 'withdrawRequestReset':
                        self.withdrawRequestReset(self.Wallet)
                    elif command == 'importContract':
                        self.importContract()
                    elif command == 'send_test_funds':
                        self.send_test_funds()
                    elif command == 'deleteAddr':
                        DeleteAddress(self, self.Wallet, arguments[0])
                    elif command == 'isRefundActive':
                        self.isRefundActive(test_fund)
                    elif command is None:
                        print('please specify a command')
                    else:
                        print("command %s not found" % command)

            except Exception as e:

                print("could not execute command: %s " % e)
                traceback.print_stack()
                traceback.print_exc()

    def send_test_funds_to(self, asset, addr_to):
        send_args = [
            asset,
            addr_to,
            str(1000),
            '--from-addr={}'.format(self.wallet_addr)
        ]
        print('send_args', send_args)
        construct_and_send(self, self.Wallet, send_args)

    def send_test_funds(self):
        self.send_test_funds_to('gas','ARMwedc4kt8EVjDVqn5EDdesGSkC7miAXY') # Creator
        time.sleep(30)
        self.send_test_funds_to('neo','ARMwedc4kt8EVjDVqn5EDdesGSkC7miAXY') # Creator
        time.sleep(30)
        self.send_test_funds_to('gas','AKn6pVS9SzJNMNwxNrS2VeNoZSt6TkbZug') # Contributor
        time.sleep(30)
        self.send_test_funds_to('neo','AKn6pVS9SzJNMNwxNrS2VeNoZSt6TkbZug') # Contributor
        time.sleep(30)
        self.send_test_funds_to('gas','ALRC1h1nWsKcxzRkBx3HPzL9JTbZ51CQgC') # Contributor2
        time.sleep(30)
        self.send_test_funds_to('neo','ALRC1h1nWsKcxzRkBx3HPzL9JTbZ51CQgC') # Contributor2

    def invokeContract(self, wallet, tx, fee, results, num_ops):
        InvokeContract(wallet, tx, fee)

        self._invoke_test_tx = tx
        self._invoke_test_tx_fee = fee
        print("Invoking Contract to Blockchain please wait...")

    def intToByteArray(self, int_input):
        return int_input.to_bytes((int_input.bit_length() + 7) // 8, 'little')

    def intFromByteArray(self, bytes_input):
        return int.from_bytes(bytes_input, 'little')

    def getFundParameter(self, wallet, fund_id, param):

        invoke_args = [
            self.neo_fund_sc,
            'GetFundParameter',
            "['{}','{}']".format(
                fund_id,
                param)
            ]

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(wallet, invoke_args)

        if tx is not None and results is not None:
            self.invokeContract(wallet, tx, fee, results, num_ops)

    def importContract(self):
        print(self.neo_fund_sc)
        self.contract_addr = ImportContractAddr(self.Wallet, [self.neo_fund_sc, self.wallet_pub]).Address

    def withdrawRequestFunds(self, wallet, fund_id, amount, args=None):
        if args:
            amount = int(args[0])

        invoke_args = [
            self.neo_fund_sc,
            'WithdrawFundsRequest',
            "['{}','{}','{}']".format(
                fund_id,
                self.wallet_addr,
                amount)
            ]

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(wallet, invoke_args)


        if tx is not None and results is not None:
            self.invokeContract(wallet, tx, fee, results, num_ops)

    def withdrawRequestReset(self, wallet):
        invoke_args = [
            self.neo_fund_sc,
            'WithdrawRequestReset',
            "['{}','','']".format(
                self.wallet_addr)
            ]

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(wallet, invoke_args)


        if tx is not None and results is not None:
            self.invokeContract(wallet, tx, fee, results, num_ops)

    def withdrawFunds(self, wallet, amount, args=None):
        if args:
            amount = int(args[0])

        # addr_from = self.neo_fund_sc_addr
        sh_from = self.neo_fund_sc

        addr_to = self.wallet_addr
        addr_to_pub = self.wallet_pub

        # Need to chang this..
        # contract = ImportContractAddr(self.Wallet, [sh_from, addr_to_pub])
        self.importContract()
        addr_from = self.contract_addr


        send_args = [
            'neo',
            addr_to,
            str(amount),
            '--from-addr={}'.format(addr_from)
        ]

        construct_and_send(self, self.Wallet, send_args)
        withdraw_args = [
            addr_from,
            'neo',
            addr_to,
            str(amount)
        ]


        # construct_contract_withdrawal(self, self.Wallet, withdraw_args)


    # ONly neo or gas for now
    def depositFunds(self, wallet, fund_id, asset_id, amount, args=None):
        if args:
            amount = int(args[0])

        if asset_id == 'neo':
            # asset_id_bytes = Blockchain.Default().SystemShare().Hash.ToBytes() # NEO asset_id
            asset_id_bytes = neo_asset_id_bytes # NEO asset_id

        elif asset_id == 'gas':
            asset_id_bytes = bytearray.fromhex(gas_asset_id_hex) # GAS asset_id


        else:
            return

        invoke_args = [
            self.neo_fund_sc,
            'DepositFunds',
            "['{}',{},'{}']".format(
                fund_id,
                asset_id_bytes,
                self.wallet_addr)
            ]

        if amount > 0:
            if asset_id == 'neo':
                invoke_args.append('--attach-neo={}'.format(amount))
            if asset_id == 'gas':
                invoke_args.append('--attach-gas={}'.format(amount))
        else:
            return

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(wallet, invoke_args)

        if tx is not None and results is not None:
            self.invokeContract(wallet, tx, fee, results, num_ops)

    def invokeOperation(self, operation, args):
        invoke_args = [
            self.neo_fund_sc,
            operation,
            args]

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(self.Wallet, invoke_args)

        if tx is not None and results is not None:
            self.invokeContract(self.Wallet, tx, fee, results, num_ops)

    def isRefundActive(self, fund_id):
        invoke_args = [
            self.neo_fund_sc,
            'IsRefundActive',
            "['{}']".format(
                fund_id)
            ]

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(self.Wallet, invoke_args)

        if tx is not None and results is not None:
            self.invokeContract(self.Wallet, tx, fee, results, num_ops)


    def createFund(self, wallet, fund_id, asset_id, withdrawal_SH, goal_amount, endtime):
        user_SH = wallet.Addresses[0]
        asset_id_bytes = Blockchain.Default().SystemShare().Hash.ToBytes() # NEO asset_id
        withdrawal_SH = user_SH
        goal_amount_bytes = binascii.hexlify(self.intToByteArray(goal_amount))
        endtime_bytes = binascii.hexlify(self.intToByteArray(endtime))

        invoke_args = [
            self.neo_fund_sc,
            'CreateFund',
            "['{}','{}',{},'{}',{},{}]".format(
                user_SH,
                fund_id,
                asset_id_bytes,
                withdrawal_SH,
                goal_amount_bytes,
                endtime_bytes)
            ]

        print(invoke_args)
        tx, fee, results, num_ops = TestInvokeContract(wallet, invoke_args)

        if tx is not None and results is not None:
            self.invokeContract(wallet, tx, fee, results, num_ops)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("-w", "--wallet", action="store", help="Wallet file (eg. .db3)", required=True)
    parser.add_argument("-c", "--config", action="store", help="Config file (eg. protocol.privnet.json)", required=True)
    parser.add_argument("-o", "--operation", action="store", help="Operation to run", required=False)
    parser.add_argument("-d", "--deploy", dest='deploy', action='store_true')
    parser.set_defaults(deploy=False)

    args = parser.parse_args()

    settings.setup(args.config)

    print("Blockchain DB path:", settings.LEVELDB_PATH)

    # Setup the Blockchain
    blockchain = LevelDBBlockchain(settings.LEVELDB_PATH)
    Blockchain.RegisterBlockchain(blockchain)
    SubscribeNotifications()

    nf = NeoFund(args.wallet, pythong_wallet_pass, args.operation, deploy=args.deploy)

    reactor.suggestThreadPoolSize(15)
    reactor.callInThread(nf.runPrompt)
    NodeLeader.Instance().Start()
    reactor.run()

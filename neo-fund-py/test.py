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
from neo.Prompt.Commands.LoadSmartContract import LoadContract, GatherContractDetails, ImportContractAddr, ImportMultiSigContractAddr, generate_deploy_script
from neo.Prompt.Notify import SubscribeNotifications
from neo.Core.Blockchain import Blockchain
from neo.Fixed8 import Fixed8
from neo.Core.TX.Transaction import TransactionOutput,ContractTransaction
from neo.SmartContract.ContractParameterContext import ContractParametersContext
from neo.Network.NodeLeader import NodeLeader
from twisted.internet import reactor, task
from neo.Settings import settings

from prompt_toolkit import prompt
from prompt_toolkit.styles import style_from_dict
from prompt_toolkit.shortcuts import print_tokens
from prompt_toolkit.token import Token
from prompt_toolkit.contrib.completers import WordCompleter
from prompt_toolkit.history import FileHistory

neo_fund_avm = '/Users/nick/Documents/Git/NeoDev/neo-fund/neo-fund-sc/neo-fund-sc/bin/Debug/neo-fund-sc.avm'
neo_fund_sc = '64da1df94e1321e767ea1a62322957ebddcfaaef'
python_wallet = '/Users/nick/Documents/Git/NeoDev/pythonWallet.db3'
pythong_wallet_pass = 'pythonwallet'

class NeoFund:
    def __init__(self, walletpath, walletpass, operation, params=None, deploy=False):
        self.start_height = Blockchain.Default().Height
        self.start_dt = datetime.datetime.utcnow()
        self.walletpath = walletpath
        self.walletpass = walletpass
        self.Wallet = None
        self.operation = operation
        self.neo_fund_sc = ''
        self.contract_script = None
        self.params = params
        self.go_on = True
        self._walletdb_loop = None
        self.deploy = deploy
        self._known_addresses = []
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
        self._walletdb_loop = task.LoopingCall(self.Wallet.ProcessBlocks)
        self._walletdb_loop.start(1)

    def invokeDeploy(self):
        if self.contract_script:
            tx, fee, results, num_ops = test_invoke(self.contract_script, self.Wallet, [])

            InvokeContract(self.Wallet, tx, fee)

            print('\nDEPLOY', results)
            print('new_sc:', self.neo_fund_sc)
            print('deploing Contract...')

    def autoDeploy(self):
        function_code = LoadContract([neo_fund_avm, '05', '05', 'True'])
        self.contract_script = generate_deploy_script(
            function_code.Script,
            'NeoFund',
            str(int(time.time())),
            'Nick',
            'nickazg@gmail.com',
            'auto deploy',
            function_code.NeedsStorage,
            ord(function_code.ReturnType),
            function_code.ParameterList
        )

        if self.contract_script is not None:
            self.neo_fund_sc = function_code.ToJson()['hash']
            print('SC Hash: ',  self.neo_fund_sc)

        return self.contract_script

    def get_completer(self):
        standard_completions = ['createFund', 'invokeDeploy', 'depositFunds', 'getFundParameter', 'quit','help','tx']

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
                    elif command == 'invokeDeploy':
                        self.invokeDeploy()
                    elif command == 'createFund':
                        self.createFund(self.Wallet, 'Fund4', 'neo', 'withdrawal_SH', 100, 9999)
                    elif command == 'getFundParameter':
                        self.getFundParameter(self.Wallet, 'Fund4', 'creatorSH')
                    elif command == 'depositFunds':
                        self.depositFunds(self.Wallet, 'Fund4', 'neo', 2)
                    elif command is None:
                        print('please specify a command')
                    else:
                        print("command %s not found" % command)

            except Exception as e:

                print("could not execute command: %s " % e)
                traceback.print_stack()
                traceback.print_exc()

    def invokeContract(self, wallet, tx, fee, results, num_ops):
        InvokeContract(wallet, tx, fee)

        self._invoke_test_tx = tx
        self._invoke_test_tx_fee = fee
        print("\n-------------------------------------------------------------------------------------------------------------------------------------")
        print("Test Invoke successful")
        print("Total operations: %s " % num_ops)
        print("Results %s " % [results[0].GetBigInteger() for item in results])
        print("Invoke TX gas cost: %s " % (tx.Gas.value / Fixed8.D))
        print("Invoke TX Fee: %s " % (fee.value / Fixed8.D))
        print("-------------------------------------------------------------------------------------------------------------------------------------\n")
        print("Invoking to Blockchain contract please wait...")

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

    # ONly neo or gas for now
    def depositFunds(self, wallet, fund_id, asset_id, amount):

        if asset_id == 'neo':
            asset_id_bytes = Blockchain.Default().SystemShare().Hash.ToBytes() # NEO asset_id

        elif asset_id == 'gas':
            asset_id_bytes = Blockchain.Default().SystemCoin().Hash.ToBytes() # GAS asset_id

        else:
            return

        user_SH = wallet.Addresses[0]

        invoke_args = [
            self.neo_fund_sc,
            'DepositFunds',
            "['{}',{},'{}']".format(
                fund_id,
                asset_id_bytes,
                user_SH)
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


    # def run(self):
    #     dbloop = task.LoopingCall(Blockchain.Default().PersistBlocks)
    #     dbloop.start(.1)
    #     Blockchain.Default().PersistBlocks()
    #
    #     self.createWallet()
    #
    #     self.contract_script = self.autoDeploy()
    #
    #     if self.contract_script is not None:
    #
    #         tx, fee, results, num_ops = test_invoke(self.contract_script, self.Wallet, [])
    #         InvokeContract(self.Wallet, tx, fee)
    #
    #         print('\nDEPLOY', results)
    #         if self.deploy:
    #             print('new_sc_script:', function_code.ToJson())
    #             print('deploing Contract...')
    #             # time.sleep(30)
    #
    #         # Creating a new fund
    #         if self.operation == 'createFund':
    #             self.createFund(self.Wallet, 'Fund4', 'neo', 'withdrawal_SH', 100, 9999)
    #
    #         if self.operation == 'depositFunds':
    #             self.depositFunds(self.Wallet, 'Fund4', 'neo', 2)
    #
    #         if self.operation == 'getFundParameter':
    #             self.getFundParameter(self.Wallet, 'Fund4', 'creatorSH')
    #
    #     self.quit()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
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

    nf = NeoFund(python_wallet, pythong_wallet_pass, args.operation, deploy=args.deploy)

    reactor.suggestThreadPoolSize(15)
    reactor.callInThread(nf.runPrompt)
    NodeLeader.Instance().Start()
    reactor.run()
